# Razer Controller for Macro Deck 3 — Design Specification

Date: 2026-10-03
Status: Draft for review

## 1. Purpose

Control Razer devices from Macro Deck 3 buttons while Razer Synapse 4 keeps
running. This covers:

- switching any device's Synapse profile
- setting a device's lighting effect
- toggling gaming mode
- stepping or temporarily setting the mouse's DPI

Variables follow what Synapse and the mouse actually report.

This replaces the Macro Deck 2 plugin in
`.oldPlugins/Razer Synaspe Controller`. That plugin handled only the mouse, and
Macro Deck 3 cannot load Macro Deck 2 plugins. The keyboard features and the
generic per-device design are new.

## 2. Scope

In scope:

- **Switch profile** for any Razer device Synapse manages: a chosen profile,
  the next one or the previous one.
- **Lighting effect** for any device that supports lighting: the effect, plus a
  colour where the effect takes one.
- **Gaming mode** for any device that has it: on, off or toggle.
- **Mouse DPI**: step up or down through the stages, or apply a temporary DPI
  value.
- **Variables** for status, the mouse and the keyboard (section 8).
- **Synapse inspector autostart setting**, ported from the old plugin.
- **Migration** of Macro Deck 2 buttons that used the old plugin.

Out of scope:

- **F13–F24 key sending.** The user does not need it.
- **Brightness.** Not requested.
- **Editing profile contents** (keybinds, macros, DPI stages). The plugin
  selects and toggles; it does not author.
- **The Razer Chroma SDK.** Its service is not running on this machine, and it
  overrides Synapse's effect rather than changing it.
- **Publishing to the Macro Deck store.** The inspector route would likely fail
  store review. The plugin is sideloaded.

## 3. Environment

| Item | Value |
| --- | --- |
| Macro Deck | 3.0.0-beta.15, `D:\Macrodeck3.0`, data in `%APPDATA%\MacroDeck` |
| SDK | `MacroDeck.Sdk`, `MacroDeck.Plugin.Hosting` and companions, **pinned to `3.0.0-beta.15`** |
| Plugin CLI | `MacroDeck.Plugin.Cli` 3.0.0-beta.15 (`macrodeck-plugin`) |
| Target | `net10.0-windows10.0.22000`, self-contained `win-x64` exe |
| Synapse | Synapse 4, `RazerAppEngine` `app-4.0.827` |
| Mouse | Razer Naga Trinity, VID `0x1532`, PID `0x0067` (product 103) |
| Keyboard | Razer Cynosa Chroma, PID `0x022A` (product 554) |
| Plugin ID | `com.keystonedigital.razer` |

The SDK version is pinned. Unlisted `3.0.0-preview.*` packages on NuGet rank
above `beta.15` under SemVer, so a floating `3.0.0-*` reference is not safe.

## 4. Proven foundations

These facts come from a live spike on 2026-10-03. Every change it made was
reverted.

### 4.1 Synapse device windows

Synapse runs one hidden renderer per device, on
`https://apps.razer.com/synapse/products/<pid>/mw/index.html?containerId=...`.

- The window title is `usb_5426_<pid>_{<containerId>}_mw`.
- That title is also the device's **BroadcastChannel name**.

### 4.2 The command envelope

Any other `https://apps.razer.com` renderer can post this message on a device's
channel:

```json
{ "msgName": "userCommand", "msgData": { "type": "<EVENT>", "payload": { } } }
```

The device window then enqueues `<EVENT>` with the source `actionFromUI`,
exactly as Synapse's own UI does.

| Event | Payload | Spike result |
| --- | --- | --- |
| `ON_SWITCH_PROFILE` | `{ newActiveProfileGUID }` | Mouse: Default to Wardogs in 101 ms, then back |
| `ON_SET_EFFECTS` | `{ effectSetting: { selectedEffectId, selectedEffectSetting? } }` | Keyboard: Static green to Spectrum, then back |
| `ON_SET_GAME_MODE` | `{ gamingMode: { ...current, state, isWindowsKeyDisabled } }` | Keyboard: on with the Windows key disabled, then off |

Lighting and gaming mode were confirmed in Synapse's stored state. Their
**physical** effect still has to be confirmed by the user (section 11).

The old plugin also posted a raw `{type: "ON_SWITCH_PROFILE"}` message. That
message lacks the envelope, so Synapse treats it as a custom UI request and
ignores it. This plugin uses only the envelope above.

### 4.3 State Synapse exposes

Synapse keeps the following in `localStorage`, shared across the
`apps.razer.com` origin:

- **`connectedDeviceInfo`** is an array of devices. Each has `productId`,
  `name.en`, `category` (`MOUSE` or `KEYBOARD`), `serialNumber` and
  `isChromaDevice`.
- **`synapse_<pid>`** holds one device's document:
  - `activeProfile`, a GUID
  - `profiles[]`, where each profile has `guid`, `name`, `quickEffects` and
    `gamingMode`
  - `deviceMetadatas[<serial>]`
- **`synapse_<pid>_profiles_stack`** holds the profile stack.

Quick-effect IDs: Off 0, Static 1, Breathing 2, Spectrum 3, Wave 4, Reactive 5,
Ripple 6, Starlight 7. Each device's window holds the list of effects it
supports in `runtimeData.supportEffects`. The plugin reads that list rather than
assuming every effect works.

### 4.4 Inherited from the old plugin (proven live 2026-09-24)

- **Connecting to the inspector.** Synapse's Node inspector is reachable at
  `127.0.0.1:9229` when `RazerAppEngine` starts with `--inspect=127.0.0.1:9229`.
  Adding that flag to the `RazerAppEngine` value under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` needs no elevation.
- **Opening the mouse.** Open the mouse's HID control interface with zero
  access rights, then use `HidD_SetFeature` and `HidD_GetFeature`.
- **The shared reply slot.** Synapse's lighting stream shares the reply slot,
  so each reply must match the command class and ID that was sent. Retry until
  it does.
- **NOSTORE writes.** Repeated writes use NOSTORE (`0x00`). VARSTORE would write
  the mouse's onboard flash.

## 5. Architecture

One Macro Deck 3 out-of-process plugin. A single integration class implements:

- `IPluginIntegration`
- `IVariableProvider`
- `IConfigFlowProvider`
- `IMigrationProvider`
- dynamic-option providers

Long-lived work runs in one hosted background service.

### 5.1 Components

Each component has one responsibility.

| Component | Responsibility | Depends on |
| --- | --- | --- |
| `InspectorConnection` | CDP WebSocket to `127.0.0.1:9229`, identity check, reconnect with backoff, connection state | — |
| `DeviceCatalog` | Finds device windows (4.1), reads Synapse state (4.3), builds the immutable `Device` snapshot | `InspectorConnection` |
| `SynapseCommands` | Builds the envelope (4.2), sends it from a non-device renderer, waits for confirmation | `InspectorConnection`, `DeviceCatalog` |
| `MouseHid` | DPI read, step and temporary set over HID (4.4), ported from the old plugin | Win32 HID |
| `InspectorAutostart` | Reads, sets and repairs the HKCU Run `--inspect` flag, ported from the old plugin | Registry |
| `RazerIntegration` | Actions, variables, settings form, migration, status | All of the above |

No component outside `InspectorConnection` speaks CDP. No component outside
`SynapseCommands` builds a script that changes anything.

### 5.2 The device model

```
Device
  Serial         string   stable identity; what buttons store
  ProductId      int
  Name           string   e.g. "Razer Cynosa Chroma"
  Category       Mouse | Keyboard | Other
  Channel        string   BroadcastChannel / window title
  Profiles       [ { Guid, Name } ]
  ActiveProfile  Guid
  Effect         { Id, Color? }        null when the device has no lighting
  SupportedEffects [ int ]
  GamingMode     { On, WindowsKeyDisabled }   null when the device has none
  Connected      bool
```

A mouse also carries `Dpi`, `DpiStage` and `DpiStageCount`, read from HID.

## 6. Data flow

### 6.1 Startup

- **`InitializeAsync` may run more than once.** It runs again after a reconnect
  or a settings change. It ensures the background service is started and
  returns.
- **The service starts with the inspector.** It connects, runs the identity
  check, then builds the first catalog.
- **HID opens separately.** It opens the control interface of each mouse it
  finds, independently of the inspector.

### 6.2 Refresh

- **Every 2 seconds** the service rebuilds the catalog, and it does so again
  right after each command.
- **Each rebuild is one inspector evaluation.** That evaluation reads
  `connectedDeviceInfo`, every `synapse_<pid>` document, each device window's
  title and its supported effects.
- **HID values update on the same cycle.**

### 6.3 Button press

1. Resolve the button's stored device serial against the current snapshot.
2. Queue the command on that device's own queue, so presses on one device run
   in order.
3. Send the command:
   - `SynapseCommands` posts the envelope. It then re-reads that device's
     document every 100 ms for up to **1.5 seconds**, until the expected change
     appears.
   - DPI commands go to `MouseHid` instead and are confirmed by reading the
     value back.
4. Return `ActionResult.Success()`, or `ActionResult.Failed(code, message)` as
   listed in section 9.

**Synapse's stored state is the only source of truth.** No variable changes
because a command was sent; it changes only when Synapse or the mouse reports
the new value.

### 6.4 Dropdowns and variables

- **`ReadAsync` and the option providers read only the cached snapshot.** They
  never wait on the inspector.
- **The profile dropdown depends on the chosen device.** It uses the same
  dependent-options pattern as the SDK sample's `PlayCatalogItemAction`.
- **The effect dropdown** lists only that device's supported effects.

## 7. Actions

Every Synapse action starts with a **Device** dynamic choice. Its options are
the connected devices, labelled with the device name, and its value is the
serial. A device that has disappeared stays selectable, labelled "(not
connected)", so a button never silently loses its setting.

| Action | Parameters | Behaviour |
| --- | --- | --- |
| Switch profile | Device; Mode (`Profile`, `Next`, `Previous`); Profile (a dynamic choice by GUID, shown only when Mode is `Profile`) | `ON_SWITCH_PROFILE`. `Next` and `Previous` wrap in Synapse's list order. |
| Lighting effect | Device; Effect (a dynamic choice from the supported effects); Colour (shown only for effects that take one) | `ON_SET_EFFECTS` with `selectedEffectId` and, where it applies, `selectedEffectSetting.color1` |
| Gaming mode | Device (only devices that have gaming mode); State (`On`, `Off`, `Toggle`) | `ON_SET_GAME_MODE`, keeping the profile's other gaming-mode fields and setting `state` and `isWindowsKeyDisabled` together |
| Step DPI | Mouse; Direction (`Up`, `Down`) | HID stage step |
| Temporary DPI | Mouse; DPI value (a number, clamped to what the mouse supports) | HID NOSTORE write. Synapse is not told, so the value lasts until the next profile switch or replug. |

Parameter visibility uses `ActionParameter.OnlyWhen`. The exact factory
signatures (`DynamicChoice`, `Choice`, `Color`, `Number`) are confirmed against
the beta.15 assembly before use, not taken from the docs.

## 8. Variables

| Variable | Type | Source |
| --- | --- | --- |
| `razer_status` | Text | Connection state: `ready`, `inspector_unavailable`, `inspector_untrusted`, `synapse_unrecognised`, `no_devices` |
| `razer_mouse_profile` | Text | Active profile name of the first connected mouse |
| `razer_mouse_dpi` | Number | Current DPI, from HID |
| `razer_mouse_dpi_stage` | Text | `stage/count`, e.g. `3/3` |
| `razer_keyboard_profile` | Text | Active profile name of the first connected keyboard |
| `razer_keyboard_effect` | Text | Effect name, e.g. `Static` |
| `razer_keyboard_gaming_mode` | Boolean | Gaming mode is on |

**A value that cannot be read reads as unavailable.** It never reads as a
misleading default.

**Per-device variables, if the SDK allows them.** If `IVariableProvider` can
change its definitions at runtime, the variables become per device instead,
named `razer_<category>_<n>_*`. This is checked against the assembly during
implementation. If it is not supported, the table above is final.

## 9. Error handling

| Situation | Status | Action result |
| --- | --- | --- |
| Synapse not running, or port 9229 closed | `inspector_unavailable`. Reconnects with backoff of 1, 2 and 4 s, capped at 10 s. | Synapse actions fail with `synapse_unavailable`. DPI keeps working. |
| The process on 9229 is not `RazerAppEngine` (`process.type` is `browser` and `execPath` ends in `razerappengine.exe`) | `inspector_untrusted` | Every Synapse action fails. Nothing is evaluated beyond the identity probe. |
| No device windows match the pattern | `synapse_unrecognised`, and the Synapse version is logged | Synapse actions fail with `synapse_unrecognised` |
| The device's serial is not in the snapshot | — | `device_not_connected` |
| The profile GUID is not in the device's profiles | — | `profile_not_found` |
| The effect is not in the device's supported effects | — | `effect_not_supported` |
| No confirmation within 1.5 s | — | `not_applied`, "Synapse did not apply the change" |
| The `--inspect` flag is set but nothing listens | `inspector_unavailable`, and the settings form explains that a Synapse restart is needed | — |

## 10. Settings and security

### 10.1 Settings form

The settings form is an `IConfigFlowProvider` with
`RequiresConfiguration => false`. It has one setting, **Enable Synapse
control**, which adds or removes the `--inspect` flag in the HKCU Run value. It
shows:

- the current state of that flag
- whether the port is actually listening
- a plain statement of the security cost (10.2)

The repair-on-startup behaviour is ported unchanged.

### 10.2 Security

While `--inspect` is set, **any local process can run code inside Synapse.** The
plugin does not create that exposure, but its setting turns it on. The settings
form and the README both say so.

The plugin's own rules:

- **Loopback only.** Connect to `127.0.0.1` only.
- **Identity first.** Run the identity probe before any other evaluation.
- **Fixed scripts.** Every script is a constant template. Every value placed
  into a script (channel, GUID, colour, state) is JSON-serialized and inserted
  as a literal, never concatenated as raw text. This rule and its tests are
  ported from the old `InspectorScripts`.
- **Data, never code.** Nothing read from Synapse's storage is executed or used
  to build code.

## 11. Testing

### 11.1 Automated

These need no hardware, no Synapse and no Macro Deck.

- **Catalog parsing.** Build the catalog from a fixture captured from this
  machine's real Synapse state, with serials masked. Also cover missing,
  partial and unrecognised documents.
- **Envelope building.** Values with quotes, back-ticks and `");` stay inert
  data.
- **Confirmation logic.** A fake inspector covers confirm, timeout and the
  device disappearing mid-command.
- **Option providers.** Dependent profile options, supported-effect filtering,
  and the "(not connected)" label.
- **Status values.** Every row of section 9.
- **The integration.** Actions, variables, the settings form and migration
  through `MacroDeck.Plugin.Testing`.
- **Ported tests.** Those that still apply from the old plugin: HID reply
  matching, NOSTORE, the autostart command line and script serialization.

### 11.2 Manual tools

- **`ManualInspectorCheck`** is a console app. It lists devices, then switches a
  profile, changes the effect and toggles gaming mode, reverting each one.
- **`ManualHidCheck`** is unchanged in purpose.

### 11.3 Acceptance

The plugin runs under the MD3 developer-mode real-host profile, on a test deck.
It is complete only when the user has confirmed each of these by hand:

- The mouse switches to a chosen profile, and to the next and previous ones.
  Synapse shows the change.
- The keyboard switches profile, once a second keyboard profile exists.
- The keyboard's lighting visibly changes to each supported effect, and the
  colour applies.
- Gaming mode on blocks the Windows key. Off restores it. Toggle alternates.
- Step DPI and Temporary DPI change the pointer's speed, and `razer_mouse_dpi`
  follows.
- Every variable shows the real value, and changes made inside Synapse appear
  within about 2 s.
- Closing Synapse sets `razer_status = inspector_unavailable`, and DPI still
  works. Reopening Synapse recovers on its own.
- An old Macro Deck 2 button that used profile or DPI migrates to a working
  Macro Deck 3 button.

A green build is not acceptance.

## 12. Migration from Macro Deck 2

The integration implements `IMigrationProvider` with
`MigrationSource.MacroDeck2`. It claims the old plugin's actions by their Macro
Deck 2 assembly name, `RazerMousePlugin`.

| Old action | New action |
| --- | --- |
| Switch Synapse profile | Switch profile, with the device and profile carried over |
| Step DPI | Step DPI |
| Temporary DPI | Temporary DPI |
| Send key (F13–F24) | Left as a placeholder (out of scope) |

The exact old type names and configuration fields are read from the archived
plugin's source during implementation.

## 13. Project layout

```
Razer Controller/
  docs/superpowers/specs/          this spec
  docs/superpowers/plans/          implementation plan
  docs/FINDINGS.md                 Synapse protocol facts (section 4), maintained
  src/RazerController/             the plugin
    manifest.json, macrodeck-build.json, Program.cs
    Synapse/  InspectorConnection, DeviceCatalog, SynapseCommands, Scripts
    Hid/      MouseHid and its interop
    Autostart/ InspectorAutostart
    Actions/  one file per action
    RazerIntegration.cs
  test/RazerController.Tests/
  tools/ManualInspectorCheck/, tools/ManualHidCheck/
  README.md
```

The old plugin's source in `.oldPlugins` is a reference for porting. It is not
modified.

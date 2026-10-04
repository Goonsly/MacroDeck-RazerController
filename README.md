# Razer Controller for Macro Deck 3

Control Razer devices from Macro Deck 3 buttons while Razer Synapse 4 keeps running:

- switch, step or cycle Synapse profiles on any Razer device
- set a device's lighting effect
- toggle a keyboard's gaming mode
- step or temporarily set the mouse's DPI

Tested on a Razer Naga Trinity and a Razer Cynosa Chroma with Synapse 4.0.827.

## Install

1. Build the package with `macrodeck-plugin build --source src/RazerController --rid win-x64 --output ./artifacts`, or use a release artifact.
2. In Macro Deck 3, install `com.keystonedigital.razer-<version>-win-x64.macroDeckPlugin`. It is not signed, so Macro Deck asks you to confirm and marks it unverified.
3. Open the plugin's settings (**Synapse control**) and turn on **Enable Synapse control**. Then restart Synapse, or sign out and back in.

Mouse DPI works without step 3. Profiles, lighting and gaming mode need it.

## Security

Synapse control adds `--inspect=127.0.0.1:9229` to Synapse's Windows startup entry, under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. That opens Synapse's Node debugging port on this PC only. While it is on, **any program running on this PC can control Razer Synapse** through that port. The plugin connects only to `127.0.0.1` and checks that the port belongs to `RazerAppEngine.exe` before using it. Turn the setting off to remove the flag.

## Actions

| Action | What it does |
| --- | --- |
| Switch Synapse profile | Picks a device, then one of: a chosen profile, next, previous, or cycle through chosen profiles |
| Set lighting effect | Off, Static (with colour), Breathing, Spectrum, Wave, Reactive, Ripple or Starlight |
| Gaming mode | On, Off or Toggle. Gaming mode disables the Windows key |
| Step mouse DPI | One DPI stage up or down within the active profile |
| Temporary mouse DPI | A DPI value until the next profile switch or DPI step |

Each Synapse action waits until Synapse confirms the change, about 1.5 seconds at most. Otherwise it reports "Synapse did not apply the change".

## Variables

| Variable | Value |
| --- | --- |
| `razer_status` | `ready`, `inspector_unavailable`, `inspector_untrusted`, `synapse_unrecognised`, `no_devices` or `starting` |
| `razer_mouse_profile` | Active profile of the first mouse |
| `razer_mouse_dpi` | Current mouse DPI |
| `razer_mouse_dpi_stage` | `stage/count`, `0/n` when on a temporary DPI |
| `razer_keyboard_profile` | Active profile of the first keyboard |
| `razer_keyboard_effect` | `Off`, `Static`, `Breathing`, `Spectrum`, `Wave`, `Reactive`, `Ripple` or `Starlight` |
| `razer_keyboard_gaming_mode` | `true` or `false` |

## Troubleshooting by `razer_status`

- **`inspector_unavailable`**: Synapse is not running or not listening on port 9229. Turn on Synapse control, then restart Synapse. The plugin reconnects on its own.
- **`inspector_untrusted`**: something other than Razer Synapse is using port 9229. The plugin refuses to talk to it.
- **`synapse_unrecognised`**: Synapse is reachable, but its device windows were not found. A Synapse update may have changed them. Check the host log in `%APPDATA%\MacroDeck\logs`.
- **`no_devices`**: Synapse reports no connected Razer devices.

## Development

```
dotnet build
dotnet test
dotnet run --project tools/ManualInspectorCheck -- --roundtrip   # live: switches and restores, needs Synapse
macrodeck-plugin test --project src/RazerController --report markdown --output conformance.md
```

`docs/superpowers/specs/` holds the design. `docs/superpowers/plans/` holds the implementation plan.

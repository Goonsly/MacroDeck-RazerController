# Macro Deck Store submission plan

Status on 2026-10-04: version 0.1.1 is tested by hand on a Naga Trinity and a Cynosa Chroma (Synapse 4.0.827). The source is public at https://github.com/Goonsly/MacroDeck-RazerController under MIT.

Sources, re-read before submitting because they change:

- Creator Guidelines: https://api.macro-deck.app/api/v1/public/creator-guidelines
- Publishing docs: https://docs.macro-deck.app/creator-portal/
- Blocked packages and minimum SDK: https://api.macro-deck.app/api/v1/public/dependency-policy/blocked-packages and https://api.macro-deck.app/api/v1/public/dependency-policy/sdk

## How a plugin reaches the Store

1. Create a Creator Portal Project of type **Plugin / Integration**. Its Package ID must equal `com.keystonedigital.razer`.
2. Connect this GitHub repository to that Project. The repository must be public.
3. Add the release workflow `.github/workflows/release.yml`. It calls `Macro-Deck-App/GitHub-Actions/.github/workflows/publish-plugin.yml@v1` with `source: src/RazerController`.
4. Publish a GitHub release, for example `v1.0.0`:
   - The tag sets the version. The workflow writes it into `manifest.json`.
   - The workflow builds the package and runs the conformance suite on a stub host.
   - It then uploads the build to the portal.
5. In the portal, select **Create release**, then **Add to submission**, then **Submit for Review**. A moderator reviews it.
6. The Store signs the package. No signing key is needed.

## Blockers to clear first

| # | Item | Why | Owner |
| --- | --- | --- | --- |
| 1 | Decide `publisher.name` | It must equal the Creator Portal owner: the Organization name, or your username for a personal Project. The manifest says `Keystone Digital`, so either create a "Keystone Digital" organization in the portal or change the manifest to your username. An upload naming anyone else is refused. | You |
| 2 | Display name that does not imply an official Razer product | Rule 5 allows naming the integration but not implying an official relationship. "Razer Controller" reads as Razer's own. Use something like "Synapse Control for Razer devices (unofficial)", and keep the Razer logo out of the icon and screenshots. | You decide, I change it |
| 3 | Listing text | Rule 10 requires prerequisites and limitations stated plainly. See "Listing draft" below. | Me, you review |
| 4 | Screenshots of the real plugin | Rule 10 forbids mockups. They must show the MD3 action editor and a deck with Razer buttons. | You capture, I can arrange them |
| 5 | Fix the deferred minor issues | Rule 6 covers reasonable care and unhandled errors. Listed below. | Me |
| 6 | `.github/workflows/release.yml` and a CI build-and-test workflow | Needed for the Store path. CI keeps every push tested. | Me |
| 7 | Version 1.0.0 | The first Store version. | Me, at release |

## Policy risk: driving Synapse through its debugging port

This is the one part a moderator may question. Rule 4 forbids "hidden backdoors or unauthorized remote access" and code that "secretly performs actions unrelated to the plugin's stated purpose".

Why the plugin stays within the rules:

- **Opt-in, never automatic.** The `--inspect` flag is added only when the user turns on **Synapse control** in the plugin's settings. Nothing is changed at install.
- **Disclosed.** The settings screen, README and Store listing all state what the flag does and that any local program can control Synapse while it is set.
- **Loopback only.** The plugin connects only to `127.0.0.1`, with no proxy. It verifies that the process is `RazerAppEngine.exe` before sending anything.
- **Limited to the stated purpose.** It sends only Synapse's own UI commands (`ON_SWITCH_PROFILE`, `ON_SET_EFFECTS`, `ON_SET_GAME_MODE`). It reads only device and profile data. Nothing leaves the PC.
- **Usable without it.** Mouse DPI works over HID with the setting off.

Two optional steps reduce the risk further:

- Ask a Creator Moderator before submitting (the portal has a conversation per submission). That avoids a decline-and-resubmit cycle.
- Add a **Remove Synapse control** button to the settings screen that also takes effect right away. Today, removing the flag needs a Synapse restart.

If the moderator declines the inspector part, the fallback is to ship a DPI-only Store version and keep the full plugin as a GitHub sideload. The code already separates the two paths.

## Manifest permissions

- `device:usb`: kept, for HID DPI.
- `process:spawn`: removed. The plugin never starts a process.
- The registry write and the loopback connection have no permission in the vocabulary. They are disclosed in the listing text instead.

## Deferred minor issues to fix before submitting

From the final code review on 2026-10-04:

1. If Synapse dies mid-press, report `NotConnected`, not a timeout.
2. Stop two simultaneous refreshes from reconnecting twice.
3. Range-check DPI numbers instead of wrapping huge values.
4. Keep a saved but deleted profile visible as "(missing)" in the editor.
5. Catch registry permission errors in the settings form.

## Listing draft

> Control Razer devices from Macro Deck while Razer Synapse 4 keeps running. Switch, step or cycle Synapse profiles on any Razer device, set lighting effects, toggle a keyboard's gaming mode, and step or temporarily set your mouse's DPI. Variables show the active profile, DPI, lighting effect and gaming mode.
>
> **Requires:** Windows, Razer Synapse 4, Macro Deck 3.
>
> **Important:** profile, lighting and gaming-mode control uses Synapse's local debugging port. Turning on "Synapse control" in the plugin settings adds `--inspect=127.0.0.1:9229` to Synapse's Windows startup entry. While it is on, any program on this PC can control Synapse. Mouse DPI works without it.
>
> **Limitations:** lighting offers Synapse's quick effects (Off, Static, Breathing, Spectrum, Wave, Reactive, Ripple, Starlight), and a device may not support all of them. Tested with a Naga Trinity and a Cynosa Chroma. Not affiliated with or endorsed by Razer Inc.

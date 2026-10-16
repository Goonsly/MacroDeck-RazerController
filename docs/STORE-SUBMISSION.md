# Macro Deck Store submission

Checked against Creator Guidelines **version 6** and the SDK policy on 2026-10-16. Re-read both before every submission, because they change:

- https://api.macro-deck.app/api/v1/public/creator-guidelines (the `X-Creator-Guidelines-Version` header gives the version)
- https://api.macro-deck.app/api/v1/public/dependency-policy/sdk (minimum SDK at that date: `3.0.0-beta.17`)
- https://api.macro-deck.app/api/v1/public/dependency-policy/blocked-packages (empty at that date)

## Status

| Item | State |
| --- | --- |
| Public GitHub repository, MIT `LICENSE` | Done: https://github.com/Goonsly/MacroDeck-RazerController |
| SDK at or above the Store minimum | Done: `3.0.0-beta.17` |
| Package ID names the developer, not Razer | Done: `com.keystonedigital.razer` |
| Display name implies no official relationship | Done: "Synapse Control for Razer (unofficial)" |
| `publisher.name` equals the Creator Portal owner | `Keystone Digital`. **You:** create the "Keystone Digital" organization in the Creator Portal |
| AI declaration (rule 9) | Done: `generatedAssets: true` (icon and text written with AI help), no runtime AI. **You:** declare the same in the Portal |
| Only needed permissions (rule 8) | Done: `device:usb` only |
| Review findings fixed (rule 6) | Done: all important and minor findings from the 2026-10-04 review |
| CI on every push | Done: `.github/workflows/ci.yml`, Windows runner |
| Store release workflow | Done: `.github/workflows/release.yml`, `build-per-platform: true` |
| Publication validation | Done: 0 errors, 0 warnings (`macrodeck-plugin validate --artifact ... --level Publication`) |
| Conformance | Done: 32 passed, 0 failed, 17 skipped (beta.17 CLI) |
| Tested on real hardware by the publisher (rule 6) | Done: Naga Trinity and Cynosa Chroma, Synapse 4.0.827 |
| Screenshots of real use (rule 10) | **You** |
| Creator Portal project and GitHub connection | **You** |
| Release `v1.0.0` | Me, after the Portal is connected |

## Your steps in the Creator Portal

1. Sign in at macro-deck.app and create the organization **Keystone Digital**.
2. In that organization, create a Project of type **Plugin / Integration** with Package ID `com.keystonedigital.razer`.
3. Under **Builds**, select **Connect GitHub**, install the Macro Deck Platform App, and choose `Goonsly/MacroDeck-RazerController`. The Portal should then find `.github/workflows/release.yml` on `main`.
4. Fill in the Store listing: use the description below, upload 2 or 3 screenshots, and set the AI declaration to match the manifest.
5. Tell me. I then publish GitHub release `v1.0.0`, which builds, tests, runs conformance and uploads the build.
6. Under **Builds**, select **Create release**, then **Add to submission**, then **Submit for Review**. Consider leaving the moderator a note (see "Note for the moderator").

Screenshots: real captures only. Useful ones are:

- the Switch Synapse profile action editor with the device and Cycle profiles
- a deck page with profile, DPI and lighting buttons showing live variable values
- the Synapse control settings screen with its warning

Crop out the desktop and anything unrelated.

## Store description

> Control Razer devices from Macro Deck while Razer Synapse 4 keeps running. Switch, step or cycle Synapse profiles on any Razer device Synapse manages, set lighting effects, toggle a keyboard's gaming mode, and step or temporarily set your mouse's DPI. Variables show the active profiles, DPI and stage, lighting effect and gaming mode, so buttons can follow what Synapse is doing.
>
> **Requires:** Windows, Razer Synapse 4, Macro Deck 3.
>
> **Setup:** open the plugin's settings and turn on **Synapse control**. This adds `--inspect=127.0.0.1:9229` to Synapse's Windows startup entry, which opens Synapse's local debugging port so the plugin can use Synapse's own profile, lighting and gaming-mode commands. Synapse sometimes removes the flag again. While Synapse control is on, the plugin puts it back and restarts Synapse once if needed, at most once every 10 minutes. Turn the setting off to stop all of this. Mouse DPI works without it.
>
> **Security:** while Synapse control is on, any program on this PC can control Synapse through that port. The plugin only connects to 127.0.0.1, checks that the port belongs to Razer Synapse, and sends nothing off the PC.
>
> **Limitations:** lighting offers Synapse's quick effects (Off, Static with colour, Breathing, Spectrum, Wave, Reactive, Ripple, Starlight), and a device may not support every one. Tested with a Razer Naga Trinity and a Razer Cynosa Chroma. Not affiliated with or endorsed by Razer Inc.

## Note for the moderator

> This plugin drives Razer Synapse 4 through Synapse's own Node.js debugging port, because Synapse has no public API for profile switching. It is opt-in: nothing changes until the user turns on "Synapse control", and the settings screen, README and this listing explain the security cost. The plugin connects to 127.0.0.1 only, verifies the process is RazerAppEngine.exe, sends only Synapse's own UI commands (switch profile, set quick effect, set gaming mode), reads only device and profile data, and sends nothing off the machine. Mouse DPI uses HID feature reports and works without the debugging port.

## Risks that remain

- **The debugging port (rule 4).** Opt-in and disclosed. A moderator may still decline. The fallback is a DPI-only Store version, with the full plugin kept on GitHub for sideloading.
- **AI-assisted code (rule 6).** It is allowed. You, as publisher, must be able to explain, debug and maintain the code. `docs/superpowers/` holds the design and plan, and every part has tests.
- **The icon (rule 10).** A plain mouse outline in Razer green, with no Razer logo. A moderator could ask for something more recognisable. Replacing it is one SVG.
- **Synapse updates.** These can change its internal messages. If profiles stop working after an update, `razer_status` shows `synapse_unrecognised`. Fix it and ship a new version, because approved versions are final.

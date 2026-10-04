# Razer Controller (Macro Deck 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A sideloadable Macro Deck 3 plugin that switches, cycles and steps Synapse profiles on any Razer device, sets lighting effects and gaming mode, and steps or temporarily sets the mouse's DPI. Variables follow the real state.

**Architecture:** One out-of-process MD3 plugin built on `MacroDeck.Sdk` 3.0.0-beta.15.

- A hosted `SynapseService` holds a Chrome DevTools Protocol session to Synapse's Node inspector (`127.0.0.1:9229`). It does three things:
  - rebuilds a device catalog from Synapse's own storage every 2 s
  - sends the proven `userCommand` envelope on each device's BroadcastChannel
  - confirms each change by re-reading Synapse's storage
- A `MouseDpiService` drives mouse DPI over HID feature reports, ported from the MD2 plugin.
- One `RazerIntegration` exposes the actions, variables and a settings flow.

**Tech Stack:**
- .NET 10, C# latest
- `MacroDeck.Sdk`, `MacroDeck.Plugin.Hosting`, `MacroDeck.Plugin.Serilog`, `MacroDeck.Localization`, `MacroDeck.Plugin.Analyzers` and `MacroDeck.Plugin.Testing`, all at 3.0.0-beta.15
- HidSharp 2.1.0 (enumeration only)
- NUnit 4
- `MacroDeck.Plugin.Cli` 3.0.0-beta.15 for packaging

**Spec:** `docs/superpowers/specs/2026-10-03-razer-controller-md3-design.md`

## Spec amendments (decided 2026-10-04, while planning)

These amendments win over the spec where they differ.

1. **No MD2 migration provider.** The user's MD2 deck has exactly one Razer button: Cycle profiles across 3 mouse profiles. The user recreates it by hand. Spec section 12 is dropped.
2. **Switch profile gains a `Cycle` mode** with a chosen list of profiles (MultiSelect). That is the user's real use.
3. **Framework-dependent packaging, not self-contained.** No WinForms means only `Microsoft.NETCore.App` is needed, which MD3 ships. This follows the template's default.
4. **Fixed variables (spec 8, first table).** Per-device runtime variables are not attempted.
5. **Lighting effects come from a fixed list:** Off 0, Static 1, Breathing 2, Spectrum 3, Wave 4, Reactive 5, Ripple 6, Starlight 7.
   - The device's supported-effect list is not reachable from outside its window.
   - Only Static takes a colour (`selectedEffectSetting.color1`).
   - An effect Synapse refuses surfaces as `not_applied` through confirmation.
6. **DPI actions take no device parameter.** They act on the first Razer mouse whose HID control interface answers.
7. **Temporary DPI has no "hold" mode.** It lasts until the next profile switch or Step DPI.

## Global Constraints

- **Pinned packages.** Every `MacroDeck.*` package is pinned with `MacroDeckSdkVersion` = `3.0.0-beta.15` in `Directory.Packages.props`. Never `3.0.0-*`.
- **Plugin identity.** Plugin id `com.keystonedigital.razer`. Publisher name `Keystone Digital`. License `MIT`. Repository `https://github.com/Goonsly/MacroDeck-RazerController`.
- **Target.** `net10.0-windows10.0.22000`. The `win-x64` entrypoint is `runtimes/win-x64/RazerController.dll`, `FrameworkDependent`, dotnet `10.0`.
- **Namespace.** Root namespace `KeystoneDigital.RazerController`. AssemblyName `RazerController`.
- **Style.** C# in `src/` and `test/` is tab-indented. No em dashes anywhere. Comments only for non-obvious constraints.
- **Localization.** Every user-facing string is a key in `Localization/Strings.resx`. Log messages stay English literals.
- **Inspector access.** Connect to `127.0.0.1` only, with no proxy. Run the identity probe before any other evaluation. Every value placed into a script is `JsonSerializer.Serialize`d and inserted as a literal.
- **Mouse writes.** HID writes use NOSTORE (`0x00`) only.
- **Async rules.** No `.Result`, `.Wait()`, `Thread.Sleep` or `async void` inside any `IActionExecutor`, `IConfigFlow` or capability type. Forward `context.CancellationToken`.
- **Action results.** An `ActionResult` is truthful. `Success` only after confirmation.
- **Build.** Builds are warning-free.
- **Commits.** Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Synapse restarts or updates while the plugin runs.** Status must go to `inspector_unavailable`, actions must fail fast, and it must reconnect without a plugin restart. Pinned in Task 5: `Reconnects_after_the_inspector_drops`.
2. **A button saved with a device that is now unplugged, or a profile since deleted.** The action must fail with `NotFound` and a clear message, never act on a different device. Pinned in Task 7: `Switch_fails_for_an_unknown_serial` and `Switch_fails_for_a_deleted_profile`.
3. **MultiSelect and parameter values arrive in several shapes** (`JsonElement` array, `string[]`, comma string, null). The cycle must work for each. Pinned in Task 7: `ParameterReader` tests.
4. **Two button presses on the same device at once.** They must not interleave, which would corrupt CDP reply matching. Pinned in Task 3: `Concurrent_evaluations_are_serialized`.
5. **Profile names and colours carrying quotes, back-ticks or `");`.** They must stay inert data in the injected script. Pinned in Task 3: `Command_script_keeps_hostile_values_inert`.

---

## File Structure

```
Razer Controller/
  Directory.Build.props            TFM, nullable, analysis level
  Directory.Packages.props         central versions, SDK pinned to beta.15
  NuGet.config                     nuget.org only
  RazerController.slnx
  .gitignore
  src/RazerController/
    RazerController.csproj
    Program.cs                     builder chain plus service registration
    manifest.json, macrodeck-build.json, Assets/icon.svg
    Properties/launchSettings.json Real Host profile
    Localization/Strings.resx      every user-facing string
    RazerIntegration.cs            actions, variables, config flow wiring
    Variables/RazerVariables.cs    definitions and ReadAsync mapping
    Settings/InspectorConfigFlow.cs
    Actions/ParameterReader.cs     tolerant parameter value reading
    Actions/DeviceOptions.cs       shared device/profile option building
    Actions/SwitchProfileAction.cs
    Actions/LightingEffectAction.cs
    Actions/GamingModeAction.cs
    Actions/StepDpiAction.cs
    Actions/TemporaryDpiAction.cs
    Synapse/SynapseInspectorException.cs
    Synapse/CdpTransport.cs        ICdpTargetFetcher, IRawDuplex(+Factory), real impls
    Synapse/TargetSelection.cs
    Synapse/IInspector.cs          IInspector + CdpInspector (serialized calls)
    Synapse/InspectorScripts.cs    Probe, Catalog, Command
    Synapse/RazerDeviceInfo.cs     device model records
    Synapse/SynapseCatalogParser.cs
    Synapse/SynapseService.cs      hosted loop, status, SendAndConfirmAsync
    Synapse/SynapseStatus.cs
    Hid/RazerReport.cs, Hid/HidFeatureReportTransport.cs, Hid/RazerHidDevice.cs,
    Hid/RazerMouseControl.cs, Hid/DpiStages.cs, Hid/DpiStepper.cs, Hid/MouseDpiService.cs
    Autostart/AutostartCommandLine.cs, Autostart/AutostartRegistry.cs
  test/RazerController.Tests/
    RazerController.Tests.csproj
    Fixtures/catalog-snapshot.json captured live, serials masked
    Hid/*Tests.cs, Synapse/*Tests.cs, Actions/*Tests.cs, IntegrationTests.cs
  tools/ManualInspectorCheck/      console app: dump catalog, live switch/effect/gaming round trip
  README.md
```

---

### Task 1: Scaffold the plugin from the template

**Files:**
- Create: `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config`, `.gitignore`, `RazerController.slnx`
- Create: `src/RazerController/RazerController.csproj`, `Program.cs`, `manifest.json`, `macrodeck-build.json`, `Assets/icon.svg`, `Properties/launchSettings.json`, `Localization/Strings.resx`, `RazerIntegration.cs`
- Create: `test/RazerController.Tests/RazerController.Tests.csproj`, `test/RazerController.Tests/IntegrationTests.cs`

**Interfaces:**
- Produces: `KeystoneDigital.RazerController.RazerIntegration : IPluginIntegration`, the `Strings` class (generated), and a project that builds and tests clean.

- [ ] **Step 1: Copy the template's shape.** Take the template from `https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template` (clone it to the scratchpad). Copy `Directory.Build.props`, `NuGet.config` and `.gitignore` as they are. Then change `Directory.Build.props`'s `<TargetFramework>` to `net10.0-windows10.0.22000`.

- [ ] **Step 2: Write `Directory.Packages.props` with the SDK pinned.**

```xml
<Project>
	<PropertyGroup>
		<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
		<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
		<!-- Pinned: unlisted 3.0.0-preview.* packages outrank beta.15 under SemVer, so a float is unsafe. -->
		<MacroDeckSdkVersion Condition="'$(MacroDeckSdkVersion)' == ''">3.0.0-beta.15</MacroDeckSdkVersion>
	</PropertyGroup>
	<ItemGroup>
		<PackageVersion Include="MacroDeck.Localization" Version="$(MacroDeckSdkVersion)" />
		<PackageVersion Include="MacroDeck.Plugin.Analyzers" Version="$(MacroDeckSdkVersion)" />
		<PackageVersion Include="MacroDeck.Plugin.Hosting" Version="$(MacroDeckSdkVersion)" />
		<PackageVersion Include="MacroDeck.Plugin.Serilog" Version="$(MacroDeckSdkVersion)" />
		<PackageVersion Include="MacroDeck.Plugin.Testing" Version="$(MacroDeckSdkVersion)" />
		<PackageVersion Include="MacroDeck.Sdk" Version="$(MacroDeckSdkVersion)" />
		<PackageVersion Include="HidSharp" Version="2.1.0" />
		<PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
		<PackageVersion Include="NUnit" Version="4.3.2" />
		<PackageVersion Include="NUnit.Analyzers" Version="4.7.0" />
		<PackageVersion Include="NUnit3TestAdapter" Version="5.0.0" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Write the plugin csproj.** Use the template's csproj with these changes:
  - `AssemblyName` is `RazerController` and `RootNamespace` is `KeystoneDigital.RazerController`.
  - `UserSecretsId` is `RazerController-PluginDevelopment`.
  - Add `<PackageReference Include="HidSharp" />`.
  - Add `<InternalsVisibleTo Include="RazerController.Tests" />`.

- [ ] **Step 4: Write `manifest.json`.**

```json
{
  "$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
  "manifestVersion": 1,
  "id": "com.keystonedigital.razer",
  "name": "Razer Controller",
  "version": "0.1.0",
  "description": "Switches Synapse profiles, lighting effects and gaming mode on any Razer device, and steps or sets mouse DPI.",
  "icon": "Assets/icon.svg",
  "entrypoints": {
    "win-x64": { "executable": "runtimes/win-x64/RazerController.dll", "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" } }
  },
  "publisher": { "name": "Keystone Digital" },
  "license": "MIT",
  "repository": "https://github.com/Goonsly/MacroDeck-RazerController",
  "compatibility": { "macroDeck": ">=3.0.0-0" },
  "permissions": ["device:usb", "process:spawn"]
}
```

`macrodeck-build.json` is the template's `win-x64` target only, with the project name `RazerController.csproj`. `launchSettings.json` is the template's file unchanged. `Assets/icon.svg` is a simple mouse glyph, a 24x24 single-path SVG.

- [ ] **Step 5: Write `Program.cs`, a minimal `RazerIntegration` (empty `Actions`) and `Strings.resx`** with one key: `Integration.Name` = `Razer Controller`.

```csharp
using KeystoneDigital.RazerController;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;

var plugin = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog)
	.RegisterIntegration<RazerIntegration>()
	.Build();

await plugin.RunAsync();
```

- [ ] **Step 6: Write the first test.** `IntegrationTests.The_plugin_builds_and_initializes` mirrors the template's test, through `PluginTestHarness.Create(b => b.UseLocalization(Strings.LocalizationCatalog).RegisterIntegration<RazerIntegration>())`.

- [ ] **Step 7: Build and test.**

Run: `dotnet build` then `dotnet test`
Expected: 0 warnings, and the test passes.

- [ ] **Step 8: Commit** with `feat: scaffold Razer Controller MD3 plugin`.

---

### Task 2: HID DPI control (ported)

**Files:**
- Create: `src/RazerController/Hid/RazerReport.cs`, `HidFeatureReportTransport.cs`, `RazerHidDevice.cs`, `RazerMouseControl.cs`, `DpiStages.cs`, `DpiStepper.cs`
- Test: `test/RazerController.Tests/Hid/HidTests.cs`, `test/RazerController.Tests/Hid/FakeTransport.cs`

**Interfaces:**
- Produces:
  - `IFeatureReportTransport { bool SetFeature(byte[]); bool GetFeature(byte[]); }`
  - `RazerHidDevice(IFeatureReportTransport, int maxAttempts = 40, int replyDelayMs = 8)` with `byte[]? Exchange(byte[] report)`
  - `RazerMouseControl(RazerHidDevice)` with `(int X,int Y)? ReadDpi()`, `DpiStages? ReadStages()`, `bool SetActiveStage(byte)` and `bool SetDpi(int x,int y)`
  - `DpiStepper.NextStage(DpiStages, int currentDpi, bool up): byte?`
  - `DpiStages.Parse(byte[])`

- [ ] **Step 1: Port the files.** Port the MD2 source verbatim from `.oldPlugins/Razer Synaspe Controller/plugin/src/RazerMousePlugin/Hardware/` (`RazerReport`, `HidFeatureReportTransport`, `RazerDevice` renamed to `RazerHidDevice`, `RazerMouseControl`, `DpiStages`). Move `DpiStepper` out of `Actions/DpiActions.cs` into its own file. Change the namespace to `KeystoneDigital.RazerController.Hid` and convert to tabs. `RazerHidDevice` keeps its `Thread.Sleep`: it is not an SDK contract type, and `MouseDpiService` (Task 6) always calls it through `Task.Run`.

- [ ] **Step 2: Port the matching tests** from `.oldPlugins/.../test/PluginTests/Program.cs` sections for `RazerReport.Crc`, `RazerDevice` reply matching (a non-matching reply is retried and a matching one is returned), `DpiStages.Parse`/`ToPayload` round trip, and `DpiStepper` (up, down, edges, off-stage temporary DPI). Write each as an NUnit `[Test]`, using `FakeTransport`, which queues replies.

```csharp
[Test]
public void Exchange_skips_replies_that_belong_to_Synapse()
{
	var report = RazerReport.Build(0x04, 0x85, 0x07, RazerReport.NoStore);
	var lighting = new byte[91]; lighting[7] = 0x0F; lighting[8] = 0x02;
	var ours = new byte[91]; ours[1] = 2; ours[7] = 0x04; ours[8] = 0x85;
	var transport = new FakeTransport(lighting, ours);

	var reply = new RazerHidDevice(transport, replyDelayMs: 0).Exchange(report);

	Assert.That(reply, Is.SameAs(ours));
	Assert.That(transport.SetCount, Is.EqualTo(2));
}

[Test]
public void Stepping_up_from_a_temporary_dpi_lands_on_the_next_higher_stage()
{
	var stages = new DpiStages(1, [new(1, 800, 800), new(2, 1600, 1600), new(3, 3200, 3200)]);
	Assert.That(DpiStepper.NextStage(stages, 1000, up: true), Is.EqualTo((byte)2));
}
```

- [ ] **Step 3: Run the tests.**

Run: `dotnet test --filter FullyQualifiedName~Hid`
Expected: all pass.

- [ ] **Step 4: Commit** with `feat: port HID DPI control`.

---

### Task 3: Inspector transport, scripts and serialized client

**Files:**
- Create: `src/RazerController/Synapse/SynapseInspectorException.cs`, `CdpTransport.cs`, `TargetSelection.cs`, `IInspector.cs`, `InspectorScripts.cs`
- Test: `test/RazerController.Tests/Synapse/InspectorTests.cs`, `test/RazerController.Tests/Synapse/FakeDuplex.cs`

**Interfaces:**
- Produces:
  - `SynapseInspectorException(string code, string message, Exception? inner = null)` with `.Code`
  - `interface IInspector { bool IsConnected { get; } Task ConnectAsync(CancellationToken ct); Task<JsonElement?> EvaluateAsync(string expression, CancellationToken ct); void Disconnect(); }`
  - `CdpInspector(ICdpTargetFetcher, IRawDuplexFactory, int port = 9229, TimeSpan? timeout = null) : IInspector, IDisposable`
  - `InspectorScripts.Probe` (string), `InspectorScripts.Catalog` (string), `InspectorScripts.Command(string channel, string eventType, object payload): string`

- [ ] **Step 1: Port the transport files.** Port `IRawDuplex.cs` and `ICdpTargetFetcher.cs` into `CdpTransport.cs`, and port `TargetSelection.cs` and `SynapseInspectorException.cs` as they are. All come from `.oldPlugins/.../Synapse/`, with the namespace changed and tabs.

- [ ] **Step 2: Port `CdpClient` as `CdpInspector`.** Keep `ConnectAsync` (target fetch, then `Runtime.enable`, then the probe, then `ValidateProbe`), `EvaluateAsync` and `CallAsync` exactly as they were. Drop `SnapshotAsync` and `SwitchProfileAsync`. Add a `SemaphoreSlim _gate = new(1, 1)` that wraps every public `ConnectAsync` and `EvaluateAsync` body (`await _gate.WaitAsync(ct)` with `try/finally Release`), because `CallAsync` reads the socket until it sees its own id, and two concurrent callers would steal each other's replies.

- [ ] **Step 3: Write `InspectorScripts`.** `Probe` and the `getElectron` import snippet are copied from the old `InspectorScripts.cs`. Then add the two scripts below.

```csharp
internal static class InspectorScripts
{
	private const string ImportSnippet = """
		const getElectron = () => {
			if (typeof process !== "undefined" && typeof process.mainModule?.require === "function") {
				try { return process.mainModule.require("electron"); } catch (_) {}
			}
			if (typeof require === "function") return require("electron");
			throw new Error("Electron import unavailable");
		};
		""";

	// Probe: copied verbatim from the MD2 plugin's InspectorScripts.Probe.

	// Lists every device middleware window and reads Synapse's storage from one apps.razer.com
	// renderer. Storage values come back as raw strings so all parsing happens in C#.
	public static readonly string Catalog = """
		(async () => {
		__IMPORT__
		const electron = getElectron();
		const all = electron.webContents.getAllWebContents().filter(wc => wc && !wc.isDestroyed());
		const windows = [];
		let reader = null;
		for (const wc of all) {
			let url; try { url = new URL(wc.getURL()); } catch (_) { continue; }
			if (url.origin !== "https://apps.razer.com") continue;
			const m = url.pathname.match(/^\/synapse\/products\/(\d+)\/mw\//);
			if (m) windows.push({ productId: Number(m[1]), title: wc.getTitle() });
			else if (!reader) reader = wc;
		}
		if (!reader) return { version: electron.app.getVersion(), windows, storage: null };
		const pids = windows.map(w => w.productId);
		const storage = await reader.executeJavaScript(`(() => {
			const pids = ${JSON.stringify(pids)};
			const out = { connectedDeviceInfo: localStorage.getItem("connectedDeviceInfo"), devices: {} };
			for (const pid of pids) out.devices[pid] = localStorage.getItem("synapse_" + pid);
			return out;
		})()`, true);
		return { version: electron.app.getVersion(), windows, storage };
		})()
		""".Replace("__IMPORT__", ImportSnippet);

	private const string CommandTemplate = """
		(async () => {
		__IMPORT__
		const target = __PAYLOAD__;
		const electron = getElectron();
		const sender = electron.webContents.getAllWebContents().find(wc => {
			if (!wc || wc.isDestroyed()) return false;
			try { const u = new URL(wc.getURL()); return u.origin === "https://apps.razer.com" && !u.pathname.includes("/mw/"); } catch (_) { return false; }
		});
		if (!sender) return { sent: false };
		const script = "(() => { const t = " + JSON.stringify(target) + "; const bc = new BroadcastChannel(t.channel); bc.postMessage({ msgName: \"userCommand\", msgData: { type: t.type, payload: t.payload } }); bc.close(); return true; })()";
		const sent = await sender.executeJavaScript(script, true);
		return { sent: sent === true };
		})()
		""";

	public static string Command(string channel, string eventType, object payload)
	{
		var target = JsonSerializer.Serialize(new { channel, type = eventType, payload });
		return CommandTemplate.Replace("__IMPORT__", ImportSnippet).Replace("__PAYLOAD__", target);
	}
}
```

- [ ] **Step 4: Write the failing tests.**

```csharp
[Test]
public void Command_script_keeps_hostile_values_inert()
{
	var hostile = "x\"); require('child_process').exec('calc'); (\"`${1}`";
	var script = InspectorScripts.Command(hostile, "ON_SWITCH_PROFILE", new { newActiveProfileGUID = hostile });

	var literal = script.Split("const target = ")[1].Split(";\n")[0];
	using var doc = JsonDocument.Parse(literal);
	Assert.That(doc.RootElement.GetProperty("channel").GetString(), Is.EqualTo(hostile));
	Assert.That(doc.RootElement.GetProperty("payload").GetProperty("newActiveProfileGUID").GetString(), Is.EqualTo(hostile));
}

[Test]
public async Task Concurrent_evaluations_are_serialized()
{
	var duplex = new FakeDuplex(echoDelayMs: 20);
	var inspector = new CdpInspector(new FakeTargetFetcher(), new FakeDuplexFactory(duplex), timeout: TimeSpan.FromSeconds(2));
	await inspector.ConnectAsync(CancellationToken.None);

	var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => inspector.EvaluateAsync($"{i}", CancellationToken.None)));

	Assert.That(results.Select(r => r!.Value.GetInt32()), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
	Assert.That(duplex.MaxConcurrentSends, Is.EqualTo(1));
}

[Test]
public void Connect_refuses_a_process_that_is_not_RazerAppEngine()
{
	var duplex = new FakeDuplex(probe: """{"processType":"browser","electronAvailable":true,"execPath":"C:\\evil\\node.exe"}""");
	var inspector = new CdpInspector(new FakeTargetFetcher(), new FakeDuplexFactory(duplex));

	var ex = Assert.ThrowsAsync<SynapseInspectorException>(() => inspector.ConnectAsync(CancellationToken.None));
	Assert.That(ex!.Code, Is.EqualTo("incompatible_synapse"));
}
```

Also port the MD2 `TargetSelection` tests: a single node target is accepted; a non-loopback host, a wrong port, or zero or two targets are rejected.

`FakeDuplex` behaviour:
- It answers `Runtime.enable` with `{}`.
- It answers the first `Runtime.evaluate` with the probe JSON. The default probe is `{"processType":"browser","electronAvailable":true,"execPath":"C:\\Program Files\\Razer\\RazerAppEngine\\app-4.0.827\\RazerAppEngine.exe"}`.
- It answers later evaluations by echoing `expression` parsed as a JSON number, after `echoDelayMs`.
- It records the peak count of overlapping sends in `MaxConcurrentSends`, counting a send as overlapping until its reply has been read.

- [ ] **Step 5: Run the tests, implement until they pass, and run them again.**

Run: `dotnet test --filter FullyQualifiedName~Synapse`
Expected: all pass.

- [ ] **Step 6: Commit** with `feat: serialized CDP inspector client and Synapse scripts`.

---

### Task 4: Device catalog parsing from a live fixture

**Files:**
- Create: `src/RazerController/Synapse/RazerDeviceInfo.cs`, `SynapseCatalogParser.cs`
- Create: `tools/ManualInspectorCheck/ManualInspectorCheck.csproj`, `tools/ManualInspectorCheck/Program.cs`
- Create: `test/RazerController.Tests/Fixtures/catalog-snapshot.json`
- Test: `test/RazerController.Tests/Synapse/CatalogParserTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum DeviceCategory { Mouse, Keyboard, Other }
public sealed record SynapseProfile(string Guid, string Name);
public sealed record LightingEffect(int Id, string? Color);
public sealed record GamingModeState(bool On, JsonElement Raw);
public sealed record RazerDeviceInfo(
	string Serial, int ProductId, string Name, DeviceCategory Category, string Channel,
	IReadOnlyList<SynapseProfile> Profiles, string? ActiveProfileGuid,
	LightingEffect? Effect, GamingModeState? GamingMode)
{
	public string? ActiveProfileName => Profiles.FirstOrDefault(p => p.Guid == ActiveProfileGuid)?.Name;
}
public sealed record CatalogParseResult(IReadOnlyList<RazerDeviceInfo> Devices, string? SynapseVersion);
public static class SynapseCatalogParser { public static CatalogParseResult Parse(JsonElement? snapshot); }
```

- [ ] **Step 1: Write `ManualInspectorCheck`.** It references the plugin project. With `--dump <path>`, it connects a `CdpInspector`, evaluates `InspectorScripts.Catalog` and writes the JSON to the path, with every serial number (`PM…`, `3220…`) replaced by `SERIAL-<n>`.

- [ ] **Step 2: Capture the fixture from live Synapse.**

Run: `dotnet run --project tools/ManualInspectorCheck -- --dump test/RazerController.Tests/Fixtures/catalog-snapshot.json`
Expected: the file holds 2 windows (product 103 and product 554), with non-null storage strings.

- [ ] **Step 3: Write the failing parser tests.**

```csharp
[Test]
public void Parses_both_live_devices()
{
	var result = SynapseCatalogParser.Parse(Fixture.Load("catalog-snapshot.json"));

	var mouse = result.Devices.Single(d => d.ProductId == 103);
	var keyboard = result.Devices.Single(d => d.ProductId == 554);
	Assert.That(mouse.Category, Is.EqualTo(DeviceCategory.Mouse));
	Assert.That(mouse.Name, Is.EqualTo("Razer Naga Trinity"));
	Assert.That(mouse.Channel, Does.StartWith("usb_5426_103_{").And.EndWith("}_mw"));
	Assert.That(mouse.Profiles, Has.Count.EqualTo(19));
	Assert.That(mouse.ActiveProfileName, Is.Not.Null);
	Assert.That(keyboard.Category, Is.EqualTo(DeviceCategory.Keyboard));
	Assert.That(keyboard.Effect, Is.Not.Null);
	Assert.That(keyboard.GamingMode, Is.Not.Null);
	Assert.That(keyboard.GamingMode!.On, Is.False);
}

[Test] public void Null_snapshot_yields_no_devices() => Assert.That(SynapseCatalogParser.Parse(null).Devices, Is.Empty);

[Test]
public void A_window_without_a_storage_document_is_skipped()
{
	var json = """{"version":"4.0.827","windows":[{"productId":999,"title":"usb_5426_999_{X}_mw"}],"storage":{"connectedDeviceInfo":"[]","devices":{"999":null}}}""";
	Assert.That(SynapseCatalogParser.Parse(JsonDocument.Parse(json).RootElement).Devices, Is.Empty);
}

[Test]
public void Malformed_storage_json_does_not_throw()
{
	var json = """{"version":"x","windows":[{"productId":103,"title":"t"}],"storage":{"connectedDeviceInfo":"{oops","devices":{"103":"[1,2"}}}""";
	Assert.That(() => SynapseCatalogParser.Parse(JsonDocument.Parse(json).RootElement), Throws.Nothing);
}
```

- [ ] **Step 4: Implement `Parse`.** Use `TryGetProperty` throughout and catch `JsonException` around every inner `JsonDocument.Parse`.
  1. For each `windows[]` entry, take `productId` and `title`. The title becomes `Channel`.
  2. Parse `storage.devices[pid]` (a JSON string). If it is missing or invalid, skip the device.
  3. `Profiles` come from `profiles[]` (`guid`, `name`). `ActiveProfileGuid` comes from `activeProfile`.
  4. On the active profile, `quickEffects.selectedEffectId` and `quickEffects.selectedEffectSetting.color1` become `Effect`. The `gamingMode` object becomes `GamingMode(On: state > 0, Raw: clone)`.
  5. Parse `storage.connectedDeviceInfo` (a JSON string array). Match on `productId` to read `name.en`, `category` (`MOUSE` or `KEYBOARD`, anything else is `Other`) and `serialNumber`.
  6. If no `connectedDeviceInfo` entry matches, fall back. The serial is the first key of `deviceMetadatas`, the name is that entry's `name`, and the category comes from the document's `category`.

- [ ] **Step 5: Run the tests until they pass.**

Run: `dotnet test --filter FullyQualifiedName~CatalogParser`
Expected: all pass.

- [ ] **Step 6: Commit** with `feat: parse Synapse device catalog`.

---

### Task 5: SynapseService (connection loop, catalog, send and confirm)

**Files:**
- Create: `src/RazerController/Synapse/SynapseStatus.cs`, `SynapseService.cs`
- Test: `test/RazerController.Tests/Synapse/SynapseServiceTests.cs`, `FakeInspector.cs`

**Interfaces:**
- Consumes: `IInspector`, `InspectorScripts.Catalog`, `InspectorScripts.Command`, `SynapseCatalogParser.Parse`
- Produces:

```csharp
public enum SynapseStatus { Starting, Ready, InspectorUnavailable, InspectorUntrusted, SynapseUnrecognised, NoDevices }
public enum CommandOutcome { Applied, NotConnected, DeviceNotFound, NotApplied }
public sealed class SynapseService : BackgroundService
{
	public SynapseService(IInspector inspector, ILogger logger, TimeProvider time, SynapseServiceOptions? options = null);
	public SynapseStatus Status { get; }
	public IReadOnlyList<RazerDeviceInfo> Devices { get; }
	public RazerDeviceInfo? FindBySerial(string serial);
	public RazerDeviceInfo? FirstOf(DeviceCategory category);
	public Task RefreshAsync(CancellationToken ct);
	public Task<CommandOutcome> SendAndConfirmAsync(string serial, string eventType, object payload,
		Func<RazerDeviceInfo, bool> isApplied, CancellationToken ct);
}
public sealed record SynapseServiceOptions(TimeSpan RefreshInterval, TimeSpan ConfirmTimeout, TimeSpan ConfirmPoll, TimeSpan MaxBackoff)
{
	public static SynapseServiceOptions Default { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
}
```

**Behaviour:**

- **`RefreshAsync`.**
  1. If the inspector is not connected, call `ConnectAsync`.
     - A `SynapseInspectorException` with code `incompatible_synapse` sets `InspectorUntrusted`.
     - Any other code sets `InspectorUnavailable`, and the method returns.
  2. Evaluate `Catalog`, parse it, and publish the devices with `Volatile.Write` of an immutable list.
  3. Set the status:
     - no windows in the snapshot: `SynapseUnrecognised`
     - no devices: `NoDevices`
     - otherwise: `Ready`
  4. Log a status change once, never per tick.
- **`ExecuteAsync`.** Loop `RefreshAsync`. After a success, wait `RefreshInterval`. After a failure, wait backoff starting at 1 s and doubling to `MaxBackoff`.
- **`SendAndConfirmAsync`.** Uses a per-serial `SemaphoreSlim` from a `ConcurrentDictionary`.
  1. Not connected or not `Ready` gives `NotConnected`. An unknown serial gives `DeviceNotFound`.
  2. Evaluate `Command(device.Channel, eventType, payload)`. A false `sent` gives `NotApplied`.
  3. Then poll: `RefreshAsync`, then check `isApplied(FindBySerial(serial))`, every `ConfirmPoll`, until `ConfirmTimeout`.
  4. Return `Applied` or `NotApplied`.

- [ ] **Step 1: Write the failing tests** against `FakeInspector`. It holds a scripted catalog `JsonElement`, can throw on connect, records commands, and has a hook that mutates the catalog when a command arrives.

```csharp
[Test]
public async Task Applied_when_the_catalog_confirms_the_change()
{
	var fake = FakeInspector.FromFixture();
	fake.OnCommand = (channel, type, payload) => fake.SetActiveProfile(103, "target-guid");
	var service = new SynapseService(fake, Log.Logger, TimeProvider.System, Fast);
	await service.RefreshAsync(default);

	var outcome = await service.SendAndConfirmAsync(MouseSerial, "ON_SWITCH_PROFILE",
		new { newActiveProfileGUID = "target-guid" }, d => d.ActiveProfileGuid == "target-guid", default);

	Assert.That(outcome, Is.EqualTo(CommandOutcome.Applied));
	Assert.That(fake.Commands.Single().Type, Is.EqualTo("ON_SWITCH_PROFILE"));
}

[Test] public async Task NotApplied_when_Synapse_never_confirms() { /* no OnCommand hook; expect NotApplied within ~ConfirmTimeout */ }
[Test] public async Task DeviceNotFound_for_an_unknown_serial() { /* expect DeviceNotFound, and no command sent */ }
[Test] public async Task Unavailable_when_the_inspector_cannot_connect() { /* connect throws inspector_unreachable -> Status InspectorUnavailable, SendAndConfirm -> NotConnected */ }
[Test] public async Task Untrusted_when_the_probe_fails() { /* connect throws incompatible_synapse -> InspectorUntrusted */ }
[Test] public async Task Reconnects_after_the_inspector_drops() { /* Ready, then evaluate throws transport_lost -> InspectorUnavailable, next RefreshAsync -> Ready */ }
```

Write each stubbed test body in full when implementing. Each comment above names the arrange, act and assert. `Fast` is `new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50))`.

- [ ] **Step 2: Run the tests.** They fail.

- [ ] **Step 3: Implement `SynapseService` and `SynapseStatus`** as described above.

- [ ] **Step 4: Run the tests.**

Run: `dotnet test --filter FullyQualifiedName~SynapseService`
Expected: all pass.

- [ ] **Step 5: Commit** with `feat: Synapse service with catalog refresh and confirmed commands`.

---

### Task 6: MouseDpiService

**Files:**
- Create: `src/RazerController/Hid/MouseDpiService.cs`
- Test: `test/RazerController.Tests/Hid/MouseDpiServiceTests.cs`

**Interfaces:**
- Consumes: `RazerMouseControl`, `DpiStepper`, `HidFeatureReportTransport.Open`
- Produces:

```csharp
public sealed record MouseDpiState(int Dpi, int StageIndex, int StageCount);
public enum DpiOutcome { Applied, NoMouse, Failed }
public sealed class MouseDpiService : IDisposable
{
	public MouseDpiService(Func<IReadOnlyList<RazerMouseControl>> openControls, ILogger logger);
	public static MouseDpiService CreateDefault(ILogger logger); // enumerates VID 0x1532 interfaces with 91-byte feature reports
	public MouseDpiState? State { get; }
	public Task RefreshAsync(CancellationToken ct);
	public Task<DpiOutcome> StepAsync(bool up, CancellationToken ct);
	public Task<DpiOutcome> SetTemporaryAsync(int dpi, CancellationToken ct);
}
```

**Behaviour:**
- **Choosing the mouse.** The first control that returns a non-null `ReadDpi()` is the mouse. It is cached and re-enumerated when a call fails.
- **Threading.** Every HID call runs inside `Task.Run`, serialized by a `SemaphoreSlim`.
- **`State`.** `StageIndex` is the 1-based position of the current DPI in the stages ordered by X, or 0 when the DPI is off-stage. This is the same calculation as the MD2 `StepDpiAction.Publish`.
- **`SetTemporaryAsync`.** Clamps to 100..`max(stage X) * 4`, with a ceiling of 20000. Uses `SetDpi(v, v)`. It is confirmed by `ReadDpi` returning that value.

- [ ] **Step 1: Write the failing tests** with a fake control list built on `FakeTransport`:
  - stepping up changes the active stage and `State` follows
  - stepping at the top stage is a no-op that still returns `Applied`
  - no controls gives `NoMouse`
  - a failed write gives `Failed`

- [ ] **Step 2: Implement until the tests pass.**

Run: `dotnet test --filter FullyQualifiedName~MouseDpi`
Expected: all pass.

- [ ] **Step 3: Commit** with `feat: mouse DPI service`.

---

### Task 7: Actions

**Files:**
- Create: `src/RazerController/Actions/ParameterReader.cs`, `DeviceOptions.cs`, `SwitchProfileAction.cs`, `LightingEffectAction.cs`, `GamingModeAction.cs`, `StepDpiAction.cs`, `TemporaryDpiAction.cs`
- Modify: `src/RazerController/Localization/Strings.resx` (keys below), `RazerIntegration.cs` (populate `Actions`)
- Test: `test/RazerController.Tests/Actions/ParameterReaderTests.cs`, `test/RazerController.Tests/Actions/ActionTests.cs`

**Interfaces:**
- Consumes: `SynapseService` (`FindBySerial`, `Devices`, `SendAndConfirmAsync`, `Status`) and `MouseDpiService`.
- Produces:
  - Action ids `switch-profile`, `lighting-effect`, `gaming-mode`, `step-dpi`, `temporary-dpi`
  - `ParameterReader.String(IReadOnlyDictionary<string, object?>, string) : string?`
  - `ParameterReader.StringList(...) : IReadOnlyList<string>`
  - `ParameterReader.Int(...) : int?`

**Parameters** (dynamic options are served by `IDynamicOptionsActionDefinition.GetDynamicOptionsAsync`, which switches on `context.ParameterName`):

| Action | Parameters |
| --- | --- |
| `switch-profile` | `device` DynamicChoice. `mode` Choice `profile`, `next`, `previous` or `cycle` (default `profile`). `profile` DynamicChoice, `.OnlyWhen("mode","profile")`. `profiles` MultiSelect with `optionsSourceId: "profiles"`, `.OnlyWhen("mode","cycle")`. |
| `lighting-effect` | `device` DynamicChoice (devices whose `Effect` is not null). `effect` Choice over the fixed list, with values `"0".."7"`. `color` Color (default `#00FF00`), `.OnlyWhen("effect","1")`. |
| `gaming-mode` | `device` DynamicChoice (devices whose `GamingMode` is not null). `state` Choice `on`, `off` or `toggle`. |
| `step-dpi` | `direction` Choice `up` or `down`. |
| `temporary-dpi` | `dpi` Number (min 100, max 20000, step 50, default 800). |

**Options:**
- **Device options:** one per device, value = `Serial`, label = `Name`. If the current parameter's serial is not in the catalog, add it with the label `Strings.Actions.Common.DeviceNotConnected(serial)`.
- **Profile options** come from the device in `context.CurrentParameters["device"]`: value = `Guid`, label = `Name`.

**Executors** (each returns `ActionResult.Failed(code, message)` with these codes):

| Situation | Code |
| --- | --- |
| Service status not `Ready` | `ActionErrorCodes.NotConnected` |
| Serial not found | `ActionErrorCodes.NotFound` |
| Profile GUID not on the device | `ActionErrorCodes.NotFound` |
| Missing or invalid parameter | `ActionErrorCodes.InvalidParameter` |
| `NotApplied` | `ActionErrorCodes.Timeout` |
| DPI `NoMouse` | `ActionErrorCodes.NotConnected` |
| DPI `Failed` | `ActionErrorCodes.ProviderError` |

Messages come from `Strings.Actions.Errors.*`.

**Executor behaviour:**
- **Switch.**
  - **Target choice.** `next` and `previous` use the device's profile order and wrap. `cycle` uses the chosen list in order: the next entry after the active one, otherwise the first.
  - **Sending.** Send `ON_SWITCH_PROFILE` with `{ newActiveProfileGUID }` and confirm with `d.ActiveProfileGuid == target`.
  - **Already active.** If the target is already active, return Success without sending.
- **Effect.** Payload `{ effectSetting = new { selectedEffectId = id, selectedEffectSetting = id == 1 ? new { color1 = color } : null } }`, serialized with `JsonIgnoreCondition.WhenWritingNull` so it is omitted when null. Confirm with `d.Effect?.Id == id`, plus a case-insensitive colour match when the id is 1.
- **Gaming.**
  - **Base object.** Start from `GamingMode.Raw` as a `Dictionary<string, JsonElement>`.
  - **Changed fields.** Set `state` (1 or 0) and `isWindowsKeyDisabled` (`state == 1`).
  - **Toggle.** Toggle uses the current `On`.
  - **Sending.** Send `ON_SET_GAME_MODE` with `{ gamingMode }`. Confirm with `d.GamingMode?.On == target`.

- [ ] **Step 1: Write the `ParameterReader` tests.** `StringList` must accept a `JsonElement` array, a `string[]`, a `List<object>`, a comma string and null (which gives an empty list). `Int` must accept an int, a long, a double, a numeric string and a `JsonElement` number.

- [ ] **Step 2: Write the action tests.** They go through `PluginTestHarness`, with `SynapseService` and `MouseDpiService` registered over fakes via `builder.Services`. If the harness cannot reach `builder.Services`, test the executors directly by constructing each action with the services. Required tests:
  - `Switch_to_a_profile_succeeds_when_confirmed`
  - `Switch_fails_for_an_unknown_serial` (`NotFound`)
  - `Switch_fails_for_a_deleted_profile` (`NotFound`)
  - `Cycle_moves_to_the_entry_after_the_active_profile`
  - `Cycle_starts_at_the_first_entry_when_the_active_profile_is_not_in_the_list`
  - `Effect_static_sends_the_colour`
  - `Gaming_toggle_turns_on_when_off`
  - `Fails_with_NotConnected_when_Synapse_is_unavailable`
  - `Profile_options_follow_the_chosen_device`
  - `Device_options_keep_a_disconnected_serial_selectable`

- [ ] **Step 3: Run the tests.** They fail.

- [ ] **Step 4: Implement the actions and add their `Strings.resx` keys**, all under `Actions.<Name>.*`, `Actions.Common.*` and `Actions.Errors.*`. Reuse `MacroDeckStrings` where one exists.

- [ ] **Step 5: Run the tests.**

Run: `dotnet test`
Expected: all pass, with 0 warnings.

- [ ] **Step 6: Commit** with `feat: profile, lighting, gaming mode and DPI actions`.

---

### Task 8: Variables, settings flow and integration wiring

**Files:**
- Create: `src/RazerController/Variables/RazerVariables.cs`, `src/RazerController/Settings/InspectorConfigFlow.cs`, `src/RazerController/Autostart/AutostartCommandLine.cs` (ported verbatim), `src/RazerController/Autostart/AutostartRegistry.cs` (ported `IAutostartRegistry` + `WindowsAutostartRegistry`)
- Modify: `RazerIntegration.cs` (implement `IVariableProvider`, `IConfigFlowProvider`), `Program.cs` (register services), `Strings.resx`
- Test: `test/RazerController.Tests/IntegrationTests.cs`, `test/RazerController.Tests/Autostart/AutostartCommandLineTests.cs` (ported)

**Interfaces:**
- Consumes: `SynapseService.Status`, `FirstOf(Mouse)` and `FirstOf(Keyboard)`; `MouseDpiService.State`; `IAutostartRegistry`.
- Produces: `RazerIntegration : IPluginIntegration, IVariableProvider, IConfigFlowProvider`.

**Variables:** each is `VariableDefinition.Eager(name, type, refreshInterval: TimeSpan.FromSeconds(2)) with { Id = localId }`.

| name | id | type | read |
| --- | --- | --- | --- |
| `razer_status` | `status` | Text | `Status` as snake_case (`ready`, `inspector_unavailable`, ...) |
| `razer_mouse_profile` | `mouse-profile` | Text | `FirstOf(Mouse)?.ActiveProfileName` |
| `razer_mouse_dpi` | `mouse-dpi` | Numeric | `MouseDpiService.State?.Dpi` |
| `razer_mouse_dpi_stage` | `mouse-dpi-stage` | Text | `"{StageIndex}/{StageCount}"` |
| `razer_keyboard_profile` | `keyboard-profile` | Text | `FirstOf(Keyboard)?.ActiveProfileName` |
| `razer_keyboard_effect` | `keyboard-effect` | Text | effect name from the fixed list |
| `razer_keyboard_gaming_mode` | `keyboard-gaming-mode` | Boolean | `GamingMode?.On` |

A null value is `VariableReading.Unavailable`.

**Wiring:**
- **`Program.cs`** registers, before `RegisterIntegration`:
  - `IInspector` as a singleton `CdpInspector(new HttpCdpTargetFetcher(), new WebSocketRawDuplexFactory())`
  - `SynapseService` as a singleton, also added as a hosted service through `AddHostedService(sp => sp.GetRequiredService<SynapseService>())`
  - `MouseDpiService` as a singleton through `CreateDefault`
  - `IAutostartRegistry` as a singleton `WindowsAutostartRegistry`
  - `TimeProvider.System`

  `RazerIntegration`'s constructor takes these services. It builds actions only, with no side effects.
- **`InitializeAsync`** is idempotent. It runs `MouseDpiService.RefreshAsync` once and returns.
- **Config flow:** `RequiresConfiguration => false`, `AllowsMultipleConfigurations => false`.
  - `StartAsync` returns one step, `inspector`. Its `Description` reports three things: whether the flag is set, the current status, and the security warning `Strings.Settings.Inspector.SecurityWarning`. Its one field is `enabled`, a Toggle defaulting to the current flag state.
  - `SubmitAsync` reads `enabled` and calls `AutostartCommandLine.AppendFlag` or `RemoveFlag` with `--inspect=127.0.0.1:9229` on the registry value. If the `RazerAppEngine` Run value is missing, it returns `ConfigFlowResult.Error` with `Strings.Settings.Inspector.NotInstalled`. Otherwise it returns `Complete("Razer Controller", { ["enabled"] = Plain("true"|"false") })`.
  - Flow tests use a fake `IAutostartRegistry`.

- [ ] **Step 1: Port the `AutostartCommandLine` tests** from the MD2 `Program.cs`: append, remove, quoted executable and boundary matching.
- [ ] **Step 2: Write the integration tests:**
  - `Variables_read_live_values`, with fakes: mouse profile `Default`, dpi 1400, stage `3/3`, keyboard effect `Static`, gaming `false`
  - `Variables_are_unavailable_when_Synapse_is_down`
  - `Config_flow_enables_the_inspect_flag`
  - `Config_flow_disables_the_inspect_flag`
  - `Config_flow_errors_when_Synapse_autostart_is_missing`
  - `Every_resx_key_resolves`, ported from the template's `LocalizationTests`
- [ ] **Step 3: Run the tests.** They fail.
- [ ] **Step 4: Implement until they pass.**

Run: `dotnet test`
Expected: all pass, with 0 warnings.

- [ ] **Step 5: Commit** with `feat: variables, inspector setting and service wiring`.

---

### Task 9: Package, install and live verification

**Files:**
- Create: `README.md` (setup, the security note, actions, variables, troubleshooting by `razer_status`)
- Modify: `tools/ManualInspectorCheck/Program.cs` (add `--roundtrip`)

- [ ] **Step 1: Add `--roundtrip` to `ManualInspectorCheck`.** It runs through `SynapseService` against live Synapse:
  1. Switch the mouse to its second profile and back.
  2. Set the keyboard effect to Spectrum and restore the original.
  3. Toggle gaming mode on and back off.

  It prints each outcome.

Run: `dotnet run --project tools/ManualInspectorCheck -- --roundtrip`
Expected: `Applied` for all six commands. The final state matches the starting state.

- [ ] **Step 2: Run the conformance suite.**

Run: `dotnet tool install --global MacroDeck.Plugin.Cli --version 3.0.0-beta.15`, then `macrodeck-plugin test --project src/RazerController --report markdown --output conformance.md`
Expected: exit 0, with no Required failures.

- [ ] **Step 3: Build the package.**

Run: `macrodeck-plugin build --source src/RazerController --rid win-x64 --output ./artifacts`
Expected: `artifacts/com.keystonedigital.razer-0.1.0.macroDeckPlugin`

- [ ] **Step 4: Sideload it.** The user opens the `.macroDeckPlugin` file in Macro Deck 3 and confirms the unverified-install prompt. Check that the host log (`%APPDATA%\MacroDeck\logs\host-*.log`) shows the plugin's session established and `razer_status = ready`.

- [ ] **Step 5: The user runs the acceptance checks** from spec 11.3, with amendments 1 and 2 applied: rebuild the MD2 cycle button as a Cycle-mode Switch profile button. Fix any failures through superpowers:systematic-debugging.

- [ ] **Step 6: Commit** with `docs: README and live round-trip check`.

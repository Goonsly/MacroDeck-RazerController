using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeystoneDigital.RazerController.Synapse;

[assembly: SupportedOSPlatform("windows")]

// Manual checks against the real Synapse on this machine. Never run by the test suite.
//   --dump <path>   write the catalog snapshot with serial numbers masked, for test fixtures
if (args is ["--dump", var path])
{
	using var fetcher = new HttpCdpTargetFetcher();
	using var inspector = new CdpInspector(fetcher, new WebSocketRawDuplexFactory());
	await inspector.ConnectAsync(CancellationToken.None);

	var snapshot = await inspector.EvaluateAsync(InspectorScripts.Catalog, CancellationToken.None);
	var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

	var serials = new Dictionary<string, string>();
	json = Regex.Replace(json, @"(PM\d{4}F\d{8,}|\d{6}H\d{8})", m =>
	{
		if (!serials.TryGetValue(m.Value, out var masked))
		{
			masked = $"SERIAL-{serials.Count + 1}";
			serials[m.Value] = masked;
		}

		return masked;
	});

	await File.WriteAllTextAsync(path, json);
	Console.WriteLine($"Wrote {path}: {json.Length} chars, {serials.Count} serial(s) masked.");
	return 0;
}

//   --roundtrip     through SynapseService: switch the mouse profile, the keyboard effect and gaming
//                   mode, restoring each; then read (never write) the mouse DPI over HID
if (args is ["--roundtrip"])
{
	using var fetcher = new HttpCdpTargetFetcher();
	using var inspector = new CdpInspector(fetcher, new WebSocketRawDuplexFactory());
	var synapse = new SynapseService(inspector, Serilog.Core.Logger.None);
	await synapse.RefreshAsync(CancellationToken.None);
	Console.WriteLine($"status={synapse.Status} synapse={synapse.SynapseVersion} devices={string.Join(", ", synapse.Devices.Select(d => $"{d.Name} [{d.Category}, {d.Profiles.Count} profiles, active '{d.ActiveProfileName}']"))}");

	async Task Step(string label, string serial, string type, object payload, Func<RazerDeviceInfo, bool> applied) =>
		Console.WriteLine($"{label}: {await synapse.SendAndConfirmAsync(serial, type, payload, applied, CancellationToken.None)}");

	if (synapse.FirstOf(DeviceCategory.Mouse) is { Profiles.Count: > 1 } mouse)
	{
		var original = mouse.ActiveProfileGuid!;
		var other = mouse.Profiles.First(p => p.Guid != original).Guid;
		await Step("mouse profile -> other", mouse.Serial, "ON_SWITCH_PROFILE", new { newActiveProfileGUID = other }, d => d.ActiveProfileGuid == other);
		await Step("mouse profile -> back", mouse.Serial, "ON_SWITCH_PROFILE", new { newActiveProfileGUID = original }, d => d.ActiveProfileGuid == original);
	}

	if (synapse.FirstOf(DeviceCategory.Keyboard) is { Effect: { } effect, GamingMode: { } gaming } keyboard)
	{
		await Step("keyboard effect -> spectrum", keyboard.Serial, "ON_SET_EFFECTS", new { effectSetting = new { selectedEffectId = 3 } }, d => d.Effect?.Id == 3);
		object restore = effect.Color is null
			? new { effectSetting = new { selectedEffectId = effect.Id } }
			: new { effectSetting = new { selectedEffectId = effect.Id, selectedEffectSetting = new { color1 = effect.Color } } };
		await Step("keyboard effect -> back", keyboard.Serial, "ON_SET_EFFECTS", restore, d => d.Effect?.Id == effect.Id);

		await Step("gaming mode -> on", keyboard.Serial, "ON_SET_GAME_MODE", new { gamingMode = KeystoneDigital.RazerController.Actions.GamingModeAction.WithState(gaming, !gaming.On) }, d => d.GamingMode?.On == !gaming.On);
		await Step("gaming mode -> back", keyboard.Serial, "ON_SET_GAME_MODE", new { gamingMode = KeystoneDigital.RazerController.Actions.GamingModeAction.WithState(gaming, gaming.On) }, d => d.GamingMode?.On == gaming.On);
	}

	using var dpi = KeystoneDigital.RazerController.Hid.MouseDpiService.CreateDefault(Serilog.Core.Logger.None);
	await dpi.RefreshAsync(CancellationToken.None);
	Console.WriteLine($"mouse dpi (read only): {dpi.State?.ToString() ?? "no mouse answered"}");
	return 0;
}

Console.WriteLine("Usage: ManualInspectorCheck --dump <path> | --roundtrip");
return 2;

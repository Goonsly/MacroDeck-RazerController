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
	var json = JsonSerializer.Serialize(TrimToParsedFields(snapshot), new JsonSerializerOptions { WriteIndented = true });

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

//   --soak <seconds> keep one inspector session, read the catalog every 2 s, print every drop
if (args is ["--soak", var secondsText] && int.TryParse(secondsText, out var seconds))
{
	using var fetcher = new HttpCdpTargetFetcher();
	using var inspector = new CdpInspector(fetcher, new WebSocketRawDuplexFactory());
	var watch = System.Diagnostics.Stopwatch.StartNew();
	var drops = 0;
	while (watch.Elapsed.TotalSeconds < seconds)
	{
		try
		{
			if (!inspector.IsConnected) await inspector.ConnectAsync(CancellationToken.None);
			await inspector.EvaluateAsync(InspectorScripts.Catalog, CancellationToken.None);
		}
		catch (SynapseInspectorException ex)
		{
			drops++;
			Console.WriteLine($"{watch.Elapsed.TotalSeconds,6:F1}s drop: {ex.Code} {ex.Message} {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
		}
		await Task.Delay(2000);
	}
	Console.WriteLine($"{drops} drop(s) in {seconds}s");
	return 0;
}

Console.WriteLine("Usage: ManualInspectorCheck --dump <path> | --roundtrip | --soak <seconds>");
return 2;

// The fixture is committed to a public repository, so it keeps only what SynapseCatalogParser reads:
// keybinds, macros, calibration and the rest of each profile stay out.
static System.Text.Json.Nodes.JsonNode? TrimToParsedFields(JsonElement? snapshot)
{
	var root = snapshot is { } value ? System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText()) : null;
	if (root?["storage"]?["devices"] is not System.Text.Json.Nodes.JsonObject devices) return root;

	foreach (var (pid, raw) in devices.ToList())
	{
		if (raw is null) continue;
		var doc = System.Text.Json.Nodes.JsonNode.Parse(raw.GetValue<string>())!.AsObject();
		var profiles = new System.Text.Json.Nodes.JsonArray();
		foreach (var profile in doc["profiles"]?.AsArray() ?? [])
		{
			var kept = new System.Text.Json.Nodes.JsonObject();
			foreach (var key in new[] { "guid", "name", "quickEffects", "gamingMode" })
			{
				if (profile?[key] is { } field) kept[key] = field.DeepClone();
			}

			profiles.Add(kept);
		}

		var trimmed = new System.Text.Json.Nodes.JsonObject
		{
			["productId"] = doc["productId"]?.DeepClone(),
			["category"] = doc["category"]?.DeepClone(),
			["activeProfile"] = doc["activeProfile"]?.DeepClone(),
			["profiles"] = profiles,
			["deviceMetadatas"] = doc["deviceMetadatas"]?.DeepClone(),
		};
		devices[pid] = trimmed.ToJsonString();
	}

	return root;
}

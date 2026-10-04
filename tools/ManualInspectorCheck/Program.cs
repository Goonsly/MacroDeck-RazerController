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

Console.WriteLine("Usage: ManualInspectorCheck --dump <path>");
return 2;

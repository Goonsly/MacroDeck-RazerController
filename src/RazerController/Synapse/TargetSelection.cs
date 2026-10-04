using System.Text.Json;

namespace KeystoneDigital.RazerController.Synapse;

internal static class TargetSelection
{
	/// <summary>
	/// The one Node target's debugger URL, which must be a plain ws:// address on 127.0.0.1 and the
	/// expected port. Anything else is refused rather than guessed at.
	/// </summary>
	public static Uri SelectSingleNodeTarget(string rawJson, int port)
	{
		JsonDocument document;

		try
		{
			document = JsonDocument.Parse(rawJson);
		}
		catch (JsonException ex)
		{
			throw new SynapseInspectorException("inspector_protocol_error", "The local inspector returned an invalid target list.", ex);
		}

		using (document)
		{
			var root = document.RootElement;

			if (root.ValueKind != JsonValueKind.Array)
			{
				throw new SynapseInspectorException("inspector_protocol_error", "The local inspector returned an invalid target list.");
			}

			var candidates = new List<string>();

			foreach (var item in root.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object) continue;

				var isNode = item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "node";
				var url = item.TryGetProperty("webSocketDebuggerUrl", out var socketUrl) && socketUrl.ValueKind == JsonValueKind.String
					? socketUrl.GetString()
					: null;

				if (isNode && !string.IsNullOrEmpty(url)) candidates.Add(url);
			}

			if (candidates.Count != 1)
			{
				throw new SynapseInspectorException("incompatible_synapse", $"Expected exactly one Node inspector target, found {candidates.Count}.");
			}

			if (!Uri.TryCreate(candidates[0], UriKind.Absolute, out var uri)
				|| uri.Scheme != "ws"
				|| uri.Host != "127.0.0.1"
				|| uri.Port != port
				|| !string.IsNullOrEmpty(uri.UserInfo)
				|| !string.IsNullOrEmpty(uri.Fragment))
			{
				throw new SynapseInspectorException("inspector_protocol_error", "The inspector advertised a non-local or invalid WebSocket address.");
			}

			return uri;
		}
	}
}

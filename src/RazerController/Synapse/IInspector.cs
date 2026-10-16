using System.Net.WebSockets;
using System.Text.Json;

namespace KeystoneDigital.RazerController.Synapse;

internal interface IInspector
{
	bool IsConnected { get; }

	Task ConnectAsync(CancellationToken cancellationToken);

	Task<JsonElement?> EvaluateAsync(string expression, CancellationToken cancellationToken);

	void Disconnect();
}

/// <summary>
/// A Chrome DevTools Protocol session to Synapse's Node inspector. Calls are serialized: each call reads
/// the socket until it sees its own reply id, so two concurrent callers would consume each other's replies.
/// </summary>
internal sealed class CdpInspector : IInspector, IDisposable
{
	private readonly ICdpTargetFetcher _targetFetcher;
	private readonly IRawDuplexFactory _duplexFactory;
	private readonly int _port;
	private readonly TimeSpan _timeout;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private IRawDuplex? _duplex;
	private int _nextId;

	public CdpInspector(ICdpTargetFetcher targetFetcher, IRawDuplexFactory duplexFactory, int port = 9229, TimeSpan? timeout = null)
	{
		_targetFetcher = targetFetcher;
		_duplexFactory = duplexFactory;
		_port = port;
		_timeout = timeout ?? TimeSpan.FromSeconds(5);
	}

	public bool IsConnected => Volatile.Read(ref _duplex) is not null;

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// A caller that waited on the gate may find another caller already connected.
			if (_duplex is not null) return;


			var rawTargets = await _targetFetcher.FetchAsync(_port, cancellationToken).ConfigureAwait(false);
			var webSocketUrl = TargetSelection.SelectSingleNodeTarget(rawTargets, _port);

			using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				// Something on the port may accept TCP and stall the upgrade while this holds the gate.
				handshake.CancelAfter(_timeout);

				try
				{
					_duplex = await _duplexFactory.ConnectAsync(webSocketUrl, handshake.Token).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					throw new SynapseInspectorException("transport_lost", "The inspector did not complete the WebSocket handshake in time.");
				}
			}

			try
			{
				await CallAsync("Runtime.enable", null, cancellationToken).ConfigureAwait(false);
				ValidateProbe(await EvaluateCoreAsync(InspectorScripts.Probe, cancellationToken).ConfigureAwait(false));
			}
			catch
			{
				Disconnect();
				throw;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<JsonElement?> EvaluateAsync(string expression, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			return await EvaluateCoreAsync(expression, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Disconnect()
	{
		Interlocked.Exchange(ref _duplex, null)?.Dispose();
	}

	public void Dispose()
	{
		Disconnect();
		_gate.Dispose();
	}

	private static void ValidateProbe(JsonElement? probe)
	{
		if (probe is not { ValueKind: JsonValueKind.Object } value)
		{
			throw new SynapseInspectorException("inspector_protocol_error", "The inspector returned an invalid capability probe.");
		}

		var processType = value.TryGetProperty("processType", out var processTypeValue) && processTypeValue.ValueKind == JsonValueKind.String
			? processTypeValue.GetString()
			: null;

		var electronAvailable = value.TryGetProperty("electronAvailable", out var electronValue)
			&& electronValue.ValueKind == JsonValueKind.True;

		var execPath = value.TryGetProperty("execPath", out var execPathValue) && execPathValue.ValueKind == JsonValueKind.String
			? execPathValue.GetString() ?? ""
			: "";

		var exeName = execPath.Replace('\\', '/').Split('/')[^1];

		if (processType != "browser" || !electronAvailable || !exeName.Equals("razerappengine.exe", StringComparison.OrdinalIgnoreCase))
		{
			throw new SynapseInspectorException("incompatible_synapse", "The inspector is not a compatible RazerAppEngine Electron browser process.");
		}
	}

	private async Task<JsonElement?> EvaluateCoreAsync(string expression, CancellationToken cancellationToken)
	{
		var result = await CallAsync("Runtime.evaluate", new { expression, awaitPromise = true, returnByValue = true }, cancellationToken).ConfigureAwait(false);

		if (result.TryGetProperty("exceptionDetails", out _))
		{
			throw new SynapseInspectorException("incompatible_synapse", "Synapse could not execute the requested script.");
		}

		if (!result.TryGetProperty("result", out var remote) || remote.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		return remote.TryGetProperty("value", out var value) ? value.Clone() : null;
	}

	private async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		if (_duplex is not { } duplex)
		{
			throw new SynapseInspectorException("transport_lost", "The inspector connection is closed.");
		}

		var id = Interlocked.Increment(ref _nextId);
		var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
		{
			["id"] = id,
			["method"] = method,
			["params"] = parameters ?? new { },
		});

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(_timeout);

		try
		{
			await duplex.SendAsync(payload, timeoutCts.Token).ConfigureAwait(false);

			while (true)
			{
				var line = await duplex.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false)
					?? throw new SynapseInspectorException("transport_lost", "The inspector disconnected.");

				using var document = ParseMessage(line);
				var root = document.RootElement;

				if (!root.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.Number
					|| !idValue.TryGetInt32(out var replyId) || replyId != id)
				{
					// An unsolicited event, not this call's reply.
					continue;
				}

				if (root.TryGetProperty("error", out _))
				{
					throw new SynapseInspectorException("inspector_protocol_error", "Synapse rejected an inspector operation.");
				}

				if (!root.TryGetProperty("result", out var result))
				{
					using var empty = JsonDocument.Parse("{}");
					return empty.RootElement.Clone();
				}

				if (result.ValueKind != JsonValueKind.Object)
				{
					throw new SynapseInspectorException("inspector_protocol_error", "Unexpected inspector result shape.");
				}

				return result.Clone();
			}
		}
		catch (SynapseInspectorException)
		{
			Disconnect();
			throw;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			Disconnect();
			throw new SynapseInspectorException("transport_lost", "The inspector did not respond in time.");
		}
		catch (OperationCanceledException)
		{
			// Cancelling a pending socket call aborts the socket, so it cannot serve the next call.
			Disconnect();
			throw;
		}
		catch (Exception ex) when (ex is IOException or WebSocketException or ObjectDisposedException)
		{
			Disconnect();
			throw new SynapseInspectorException("transport_lost", "Could not communicate with Synapse's local inspector.", ex);
		}
	}

	private static JsonDocument ParseMessage(string line)
	{
		try
		{
			var document = JsonDocument.Parse(line);
			if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
			document.Dispose();
		}
		catch (JsonException ex)
		{
			throw new SynapseInspectorException("transport_lost", "The inspector sent an invalid message.", ex);
		}

		throw new SynapseInspectorException("transport_lost", "The inspector sent an invalid message.");
	}
}

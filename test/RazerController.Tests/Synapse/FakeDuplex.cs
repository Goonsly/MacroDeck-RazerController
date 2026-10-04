using System.Text.Json;
using KeystoneDigital.RazerController.Synapse;

namespace KeystoneDigital.RazerController.Tests.Synapse;

internal sealed class FakeTargetFetcher : ICdpTargetFetcher
{
	public Task<string> FetchAsync(int port, CancellationToken cancellationToken) =>
		Task.FromResult($$"""[{"type":"node","webSocketDebuggerUrl":"ws://127.0.0.1:{{port}}/abc"}]""");
}

internal sealed class FakeDuplexFactory(FakeDuplex duplex) : IRawDuplexFactory
{
	public Task<IRawDuplex> ConnectAsync(Uri webSocketDebuggerUrl, CancellationToken cancellationToken) =>
		Task.FromResult<IRawDuplex>(duplex);
}

/// <summary>
/// Answers Runtime.enable with {}, the first Runtime.evaluate with the probe, and every later
/// evaluation by echoing its expression parsed as JSON. Tracks how many sends overlap, counting a send
/// as open until its reply has been read.
/// </summary>
internal sealed class FakeDuplex : IRawDuplex
{
	public const string GoodProbe = """{"processType":"browser","electronAvailable":true,"execPath":"C:\\Program Files\\Razer\\RazerAppEngine\\app-4.0.827\\RazerAppEngine.exe"}""";

	private readonly string _probe;
	private readonly int _echoDelayMs;
	private readonly Queue<string> _outbox = new();
	private readonly SemaphoreSlim _available = new(0);
	private bool _probed;
	private int _open;

	public FakeDuplex(string probe = GoodProbe, int echoDelayMs = 0)
	{
		_probe = probe;
		_echoDelayMs = echoDelayMs;
	}

	public int MaxConcurrentSends { get; private set; }

	public List<string> Expressions { get; } = [];

	public Func<string, string>? Evaluator { get; set; }

	public async Task SendAsync(string message, CancellationToken cancellationToken)
	{
		var open = Interlocked.Increment(ref _open);
		lock (_outbox) MaxConcurrentSends = Math.Max(MaxConcurrentSends, open);

		using var doc = JsonDocument.Parse(message);
		var id = doc.RootElement.GetProperty("id").GetInt32();
		var method = doc.RootElement.GetProperty("method").GetString();
		string result;

		if (method == "Runtime.evaluate")
		{
			var expression = doc.RootElement.GetProperty("params").GetProperty("expression").GetString()!;
			string value;
			if (!_probed)
			{
				_probed = true;
				value = _probe;
			}
			else
			{
				Expressions.Add(expression);
				if (_echoDelayMs > 0) await Task.Delay(_echoDelayMs, cancellationToken);
				value = Evaluator?.Invoke(expression) ?? expression;
			}

			result = $$$"""{"result":{"type":"object","value":{{{value}}}}}""";
		}
		else
		{
			result = "{}";
		}

		lock (_outbox) _outbox.Enqueue($$$"""{"id":{{{id}}},"result":{{{result}}}}""");
		_available.Release();
	}

	public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
	{
		await _available.WaitAsync(cancellationToken);
		string reply;
		lock (_outbox) reply = _outbox.Dequeue();
		Interlocked.Decrement(ref _open);
		return reply;
	}

	public void Dispose()
	{
	}
}

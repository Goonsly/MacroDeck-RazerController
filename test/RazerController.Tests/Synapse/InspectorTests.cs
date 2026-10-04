using System.Text.Json;
using KeystoneDigital.RazerController.Synapse;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Synapse;

[TestFixture]
public sealed class InspectorTests
{
	[Test]
	public void Command_script_keeps_hostile_values_inert()
	{
		const string hostile = "x\"); require('child_process').exec('calc'); (\"`${1}`\u2028";
		var script = InspectorScripts.Command(hostile, "ON_SWITCH_PROFILE", new { newActiveProfileGUID = hostile });

		var afterTarget = script.Split("const target = ", 2)[1];
		var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(afterTarget), new JsonReaderOptions { AllowTrailingCommas = false });
		var target = JsonElement.ParseValue(ref reader);
		var rest = afterTarget[(int)reader.BytesConsumed..];

		Assert.That(rest.TrimStart(), Does.StartWith(";"));
		Assert.That(target.GetProperty("channel").GetString(), Is.EqualTo(hostile));
		Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("ON_SWITCH_PROFILE"));
		Assert.That(target.GetProperty("payload").GetProperty("newActiveProfileGUID").GetString(), Is.EqualTo(hostile));
	}

	[Test]
	public async Task Concurrent_evaluations_are_serialized()
	{
		var duplex = new FakeDuplex(echoDelayMs: 20);
		using var inspector = new CdpInspector(new FakeTargetFetcher(), new FakeDuplexFactory(duplex), timeout: TimeSpan.FromSeconds(2));
		await inspector.ConnectAsync(CancellationToken.None);

		var results = await Task.WhenAll(Enumerable.Range(0, 5)
			.Select(i => inspector.EvaluateAsync(i.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None)));

		Assert.That(results.Select(r => r!.Value.GetInt32()).Order(), Is.EqualTo(Enumerable.Range(0, 5)));
		Assert.That(duplex.MaxConcurrentSends, Is.EqualTo(1));
	}

	[Test]
	public void Connect_refuses_a_process_that_is_not_RazerAppEngine()
	{
		var duplex = new FakeDuplex(probe: """{"processType":"browser","electronAvailable":true,"execPath":"C:\\evil\\node.exe"}""");
		using var inspector = new CdpInspector(new FakeTargetFetcher(), new FakeDuplexFactory(duplex));

		var ex = Assert.ThrowsAsync<SynapseInspectorException>(() => inspector.ConnectAsync(CancellationToken.None));
		Assert.That(ex!.Code, Is.EqualTo("incompatible_synapse"));
		Assert.That(inspector.IsConnected, Is.False);
	}

	[Test]
	public async Task Connect_accepts_RazerAppEngine()
	{
		using var inspector = new CdpInspector(new FakeTargetFetcher(), new FakeDuplexFactory(new FakeDuplex()));

		await inspector.ConnectAsync(CancellationToken.None);

		Assert.That(inspector.IsConnected, Is.True);
	}

	[Test]
	public async Task A_cancelled_call_drops_the_connection_so_the_next_call_reconnects()
	{
		var duplex = new FakeDuplex(echoDelayMs: 2000);
		using var inspector = new CdpInspector(new FakeTargetFetcher(), new FakeDuplexFactory(duplex));
		await inspector.ConnectAsync(CancellationToken.None);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

		Assert.CatchAsync<OperationCanceledException>(() => inspector.EvaluateAsync("1", cts.Token));
		Assert.That(inspector.IsConnected, Is.False);
	}

	[Test]
	public void Connect_times_out_when_the_handshake_stalls()
	{
		using var inspector = new CdpInspector(new FakeTargetFetcher(), new StallingDuplexFactory(), timeout: TimeSpan.FromMilliseconds(200));
		var watch = System.Diagnostics.Stopwatch.StartNew();

		var ex = Assert.ThrowsAsync<SynapseInspectorException>(() => inspector.ConnectAsync(CancellationToken.None));

		Assert.That(ex!.Code, Is.EqualTo("transport_lost"));
		Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
	}

	private sealed class StallingDuplexFactory : IRawDuplexFactory
	{
		public async Task<IRawDuplex> ConnectAsync(Uri webSocketDebuggerUrl, CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new InvalidOperationException("unreachable");
		}
	}

	[Test]
	public void Evaluate_before_connect_throws_transport_lost()
	{
		using var inspector = new CdpInspector(new FakeTargetFetcher(), new FakeDuplexFactory(new FakeDuplex()));

		var ex = Assert.ThrowsAsync<SynapseInspectorException>(() => inspector.EvaluateAsync("1", CancellationToken.None));
		Assert.That(ex!.Code, Is.EqualTo("transport_lost"));
	}

	[Test]
	public void A_single_loopback_node_target_is_selected()
	{
		var uri = TargetSelection.SelectSingleNodeTarget("""[{"type":"node","webSocketDebuggerUrl":"ws://127.0.0.1:9229/x"}]""", 9229);
		Assert.That(uri.ToString(), Is.EqualTo("ws://127.0.0.1:9229/x"));
	}

	[TestCase("""[{"type":"node","webSocketDebuggerUrl":"ws://10.0.0.5:9229/x"}]""")]
	[TestCase("""[{"type":"node","webSocketDebuggerUrl":"ws://127.0.0.1:9999/x"}]""")]
	[TestCase("""[]""")]
	[TestCase("""[{"type":"node","webSocketDebuggerUrl":"ws://127.0.0.1:9229/a"},{"type":"node","webSocketDebuggerUrl":"ws://127.0.0.1:9229/b"}]""")]
	[TestCase("""not json""")]
	public void Unsafe_or_ambiguous_target_lists_are_rejected(string json)
	{
		Assert.Throws<SynapseInspectorException>(() => TargetSelection.SelectSingleNodeTarget(json, 9229));
	}
}

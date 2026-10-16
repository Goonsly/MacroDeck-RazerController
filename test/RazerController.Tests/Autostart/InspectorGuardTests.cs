using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Synapse;
using KeystoneDigital.RazerController.Tests.Synapse;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Autostart;

[TestFixture]
public sealed class InspectorGuardTests
{
	private const string Bare = "\"C:\\Program Files\\Razer\\RazerAppEngine\\RazerAppEngine.exe\" --url-params=apps=synapse --autoStart=1";
	private const string Flagged = Bare + " --inspect=127.0.0.1:9229";

	private sealed class FakeIntent(bool? value) : ISynapseControlIntent
	{
		public bool? Value { get; private set; } = value;

		public bool? Get() => Value;

		public void Set(bool enabled) => Value = enabled;
	}

	private sealed class FakeProcess(SynapseProcessState state) : ISynapseProcess
	{
		public SynapseProcessState State { get; set; } = state;

		public List<string> Restarts { get; } = [];

		public SynapseProcessState GetState() => State;

		public Task RestartAsync(string commandLine, CancellationToken cancellationToken)
		{
			Restarts.Add(commandLine);
			State = SynapseProcessState.RunningWithFlag;
			return Task.CompletedTask;
		}
	}

	private sealed class ManualClock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static InspectorGuard Guard(FakeAutostartRegistry registry, FakeIntent intent, FakeProcess process, TimeProvider? clock = null) =>
		new(registry, intent, process, SynapseServiceTests.Log, clock ?? TimeProvider.System);

	[Test]
	public async Task Repairs_the_flag_and_restarts_Synapse_when_control_is_on()
	{
		var registry = new FakeAutostartRegistry(Bare);
		var process = new FakeProcess(SynapseProcessState.RunningWithoutFlag);

		await Guard(registry, new FakeIntent(true), process).EnsureAsync(SynapseStatus.InspectorUnavailable, default);

		Assert.That(registry.Value, Is.EqualTo(Flagged));
		Assert.That(process.Restarts, Is.EqualTo(new[] { Flagged }));
	}

	[Test]
	public async Task Does_nothing_when_control_was_never_enabled()
	{
		var registry = new FakeAutostartRegistry(Bare);
		var process = new FakeProcess(SynapseProcessState.RunningWithoutFlag);

		await Guard(registry, new FakeIntent(null), process).EnsureAsync(SynapseStatus.InspectorUnavailable, default);

		Assert.That(registry.Value, Is.EqualTo(Bare));
		Assert.That(process.Restarts, Is.Empty);
	}

	[Test]
	public async Task Does_nothing_when_control_was_turned_off()
	{
		var registry = new FakeAutostartRegistry(Bare);
		var process = new FakeProcess(SynapseProcessState.RunningWithoutFlag);

		await Guard(registry, new FakeIntent(false), process).EnsureAsync(SynapseStatus.InspectorUnavailable, default);

		Assert.That(registry.Value, Is.EqualTo(Bare));
		Assert.That(process.Restarts, Is.Empty);
	}

	[Test]
	public async Task Repairs_without_restarting_when_Synapse_is_not_running()
	{
		var registry = new FakeAutostartRegistry(Bare);
		var process = new FakeProcess(SynapseProcessState.NotRunning);

		await Guard(registry, new FakeIntent(true), process).EnsureAsync(SynapseStatus.InspectorUnavailable, default);

		Assert.That(registry.Value, Is.EqualTo(Flagged));
		Assert.That(process.Restarts, Is.Empty);
	}

	[Test]
	public async Task Does_not_restart_a_Synapse_that_already_has_the_flag()
	{
		var registry = new FakeAutostartRegistry(Flagged);
		var process = new FakeProcess(SynapseProcessState.RunningWithFlag);

		await Guard(registry, new FakeIntent(true), process).EnsureAsync(SynapseStatus.InspectorUnavailable, default);

		Assert.That(process.Restarts, Is.Empty);
	}

	[Test]
	public async Task Restarts_at_most_once_per_cooldown()
	{
		var registry = new FakeAutostartRegistry(Bare);
		var process = new FakeProcess(SynapseProcessState.RunningWithoutFlag);
		var clock = new ManualClock();
		var guard = Guard(registry, new FakeIntent(true), process, clock);

		await guard.EnsureAsync(SynapseStatus.InspectorUnavailable, default);
		process.State = SynapseProcessState.RunningWithoutFlag;
		clock.Now += TimeSpan.FromMinutes(1);
		await guard.EnsureAsync(SynapseStatus.InspectorUnavailable, default);
		clock.Now += InspectorGuard.RestartCooldown;
		await guard.EnsureAsync(SynapseStatus.InspectorUnavailable, default);

		Assert.That(process.Restarts, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task A_working_connection_records_consent_once()
	{
		var intent = new FakeIntent(null);

		await Guard(new FakeAutostartRegistry(Flagged), intent, new FakeProcess(SynapseProcessState.RunningWithFlag)).EnsureAsync(SynapseStatus.Ready, default);

		Assert.That(intent.Value, Is.True);
	}

	[Test]
	public async Task A_working_connection_does_not_override_an_explicit_off()
	{
		var intent = new FakeIntent(false);

		await Guard(new FakeAutostartRegistry(Flagged), intent, new FakeProcess(SynapseProcessState.RunningWithFlag)).EnsureAsync(SynapseStatus.Ready, default);

		Assert.That(intent.Value, Is.False);
	}

	[Test]
	public async Task Missing_Synapse_install_is_left_alone()
	{
		var registry = new FakeAutostartRegistry(null);
		var process = new FakeProcess(SynapseProcessState.NotRunning);

		await Guard(registry, new FakeIntent(true), process).EnsureAsync(SynapseStatus.InspectorUnavailable, default);

		Assert.That(registry.Value, Is.Null);
		Assert.That(process.Restarts, Is.Empty);
	}

	[Test]
	public void File_intent_round_trips_and_starts_unknown()
	{
		var dir = Path.Combine(Path.GetTempPath(), "razer-intent-" + Guid.NewGuid().ToString("N"));
		try
		{
			var intent = new FileSynapseControlIntent(dir);
			Assert.That(intent.Get(), Is.Null);

			intent.Set(true);
			Assert.That(new FileSynapseControlIntent(dir).Get(), Is.True);

			intent.Set(false);
			Assert.That(new FileSynapseControlIntent(dir).Get(), Is.False);
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}
}

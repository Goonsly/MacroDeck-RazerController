using System.Text.Json;
using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Settings;
using KeystoneDigital.RazerController.Synapse;
using KeystoneDigital.RazerController.Tests.Actions;
using KeystoneDigital.RazerController.Tests.Hid;
using KeystoneDigital.RazerController.Tests.Synapse;
using MacroDeck.Sdk.ConfigFlow;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests;

[TestFixture]
public sealed class VariablesAndSettingsTests
{
	private const string RunValue = "\"C:\\Program Files\\Razer\\RazerAppEngine\\RazerAppEngine.exe\" --url-params=apps=synapse --autoStart=1";

	private static string? ValueOf(MacroDeck.Plugin.Testing.CapabilityInvocationOutcome outcome)
	{
		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		var data = outcome.Data!.Value;
		var value = data.TryGetProperty("value", out var inner) ? inner : data;
		if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var nested)) value = nested;

		// Values travel as a tagged union, e.g. {"kind":"text","text":"ready"}.
		if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("kind", out _))
		{
			value = value.EnumerateObject().Where(p => p.Name != "kind").Select(p => p.Value).FirstOrDefault();
		}
		return value.ValueKind switch
		{
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			JsonValueKind.String => value.GetString(),
			_ => value.GetRawText(),
		};
	}

	[Test]
	public async Task Variables_read_live_values()
	{
		var fake = FakeInspector.FromFixture();
		var synapse = SynapseServiceTests.CreateService(fake);
		await synapse.RefreshAsync(default);
		await using var harness = ActionTests.CreateHarness(synapse, MouseDpiServiceTests.ServiceFor(new SimulatedMouse(800, 1600, 3200) { Dpi = 3200 }));
		await harness.InitializeIntegrationsAsync();

		Assert.That(ValueOf(await harness.Variables.GetAsync("status")), Is.EqualTo("ready"));
		Assert.That(ValueOf(await harness.Variables.GetAsync("mouse-profile")), Is.EqualTo(synapse.FirstOf(DeviceCategory.Mouse)!.ActiveProfileName));
		Assert.That(ValueOf(await harness.Variables.GetAsync("mouse-dpi")), Is.EqualTo("3200"));
		Assert.That(ValueOf(await harness.Variables.GetAsync("mouse-dpi-stage")), Is.EqualTo("3/3"));
		Assert.That(ValueOf(await harness.Variables.GetAsync("keyboard-profile")), Is.EqualTo("Desktop | Default"));
		Assert.That(ValueOf(await harness.Variables.GetAsync("keyboard-effect")), Is.EqualTo("Static"));
		Assert.That(ValueOf(await harness.Variables.GetAsync("keyboard-gaming-mode")), Is.EqualTo("false"));
	}

	[Test]
	public async Task Variables_are_unavailable_when_Synapse_is_down()
	{
		var fake = FakeInspector.FromFixture();
		fake.ConnectError = new SynapseInspectorException("inspector_unreachable", "down");
		var synapse = SynapseServiceTests.CreateService(fake);
		await synapse.RefreshAsync(default);
		await using var harness = ActionTests.CreateHarness(synapse, MouseDpiServiceTests.ServiceFor(null));
		await harness.InitializeIntegrationsAsync();

		Assert.That(ValueOf(await harness.Variables.GetAsync("status")), Is.EqualTo("inspector_unavailable"));
		Assert.That(ValueOf(await harness.Variables.GetAsync("keyboard-profile")), Is.Null);
		Assert.That(ValueOf(await harness.Variables.GetAsync("mouse-dpi")), Is.Null);
	}

	private sealed class MemoryIntent : ISynapseControlIntent
	{
		public bool? Value { get; private set; }

		public bool? Get() => Value;

		public void Set(bool enabled) => Value = enabled;
	}

	private static InspectorConfigFlow Flow(FakeAutostartRegistry registry, MemoryIntent? intent = null) =>
		Flow(registry, intent ?? new MemoryIntent(), new KeystoneDigital.RazerController.Tests.Autostart.FakeProcess(SynapseProcessState.NotRunning));

	private static InspectorConfigFlow Flow(IAutostartRegistry registry, ISynapseControlIntent intent, ISynapseProcess process) =>
		new(registry, SynapseServiceTests.CreateService(FakeInspector.FromFixture()), intent, new InspectorGuard(registry, intent, process, SynapseServiceTests.Log), process);

	[Test]
	public async Task Config_flow_restarts_Synapse_with_the_flag_only_when_asked()
	{
		var registry = new FakeAutostartRegistry(RunValue);
		var process = new KeystoneDigital.RazerController.Tests.Autostart.FakeProcess(SynapseProcessState.RunningWithoutFlag);
		var flow = Flow(registry, new MemoryIntent(), process);

		await flow.SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = true }, null!, default);
		Assert.That(process.Restarts, Is.Empty);

		var result = await flow.SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = true, ["restart"] = true }, null!, default);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
		Assert.That(process.Restarts, Is.EqualTo(new[] { RunValue + " --inspect=127.0.0.1:9229" }));
	}

	[Test]
	public async Task Config_flow_turning_off_with_restart_closes_the_port_now()
	{
		var registry = new FakeAutostartRegistry(RunValue + " --inspect=127.0.0.1:9229");
		var process = new KeystoneDigital.RazerController.Tests.Autostart.FakeProcess(SynapseProcessState.RunningWithFlag);

		await Flow(registry, new MemoryIntent(), process).SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = false, ["restart"] = JsonSerializer.SerializeToElement(true) }, null!, default);

		Assert.That(process.Restarts, Is.EqualTo(new[] { RunValue }));
	}

	[Test]
	public async Task Config_flow_reports_a_failed_restart_after_saving()
	{
		var registry = new FakeAutostartRegistry(RunValue);
		var intent = new MemoryIntent();

		var result = await Flow(registry, intent, new FailingProcess()).SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = true, ["restart"] = true }, null!, default);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
		Assert.That(intent.Value, Is.True);
		Assert.That(registry.Value, Is.EqualTo(RunValue + " --inspect=127.0.0.1:9229"));
	}

	private sealed class FailingProcess : ISynapseProcess
	{
		public SynapseProcessState GetState() => SynapseProcessState.NotRunning;

		public Task RestartAsync(string commandLine, CancellationToken cancellationToken) => throw new InvalidOperationException("refused");
	}

	[Test]
	public async Task Config_flow_records_the_users_choice()
	{
		var intent = new MemoryIntent();
		var flow = Flow(new FakeAutostartRegistry(RunValue), intent);

		await flow.SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = true }, null!, default);
		var afterOn = intent.Value;
		await flow.SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = false }, null!, default);

		Assert.That(afterOn, Is.True);
		Assert.That(intent.Value, Is.False);
	}

	[Test]
	public async Task Config_flow_does_not_count_a_hand_added_flag_as_on()
	{
		var result = await Flow(new FakeAutostartRegistry(RunValue + " --inspect=127.0.0.1:9229")).StartAsync(null!, default);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
		Assert.That(result.NextStep!.Fields[0].DefaultValue, Is.EqualTo(false));
	}

	[Test]
	public async Task Config_flow_offers_the_users_choice()
	{
		var intent = new MemoryIntent();
		intent.Set(true);

		var result = await Flow(new FakeAutostartRegistry(RunValue + " --inspect=127.0.0.1:9229"), intent).StartAsync(null!, default);

		Assert.That(result.NextStep!.Fields[0].DefaultValue, Is.EqualTo(true));
		Assert.That(result.NextStep!.Fields[1].DefaultValue, Is.EqualTo(false), "restart is never pre-ticked");
	}

	[Test]
	public async Task Config_flow_explains_the_security_cost_and_the_startup_change()
	{
		var result = await Flow(new FakeAutostartRegistry(RunValue)).StartAsync(null!, default);

		Assert.That(result.NextStep!.Instructions, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task Config_flow_enables_the_inspect_flag()
	{
		var registry = new FakeAutostartRegistry(RunValue);

		var result = await Flow(registry).SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = true }, null!, default);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
		Assert.That(registry.Value, Is.EqualTo(RunValue + " --inspect=127.0.0.1:9229"));
	}

	[Test]
	public async Task Config_flow_disables_the_inspect_flag()
	{
		var registry = new FakeAutostartRegistry(RunValue + " --inspect=127.0.0.1:9229");

		await Flow(registry).SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = JsonSerializer.SerializeToElement(false) }, null!, default);

		Assert.That(registry.Value, Is.EqualTo(RunValue));
	}

	[Test]
	public async Task Config_flow_errors_when_Synapse_autostart_is_missing()
	{
		var registry = new FakeAutostartRegistry(null);

		var result = await Flow(registry).SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = true }, null!, default);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
		Assert.That(registry.Value, Is.Null);
	}

	private sealed class LockedRegistry(string value) : IAutostartRegistry
	{
		public string? GetValue() => value;

		public void SetValue(string value) => throw new UnauthorizedAccessException("denied");
	}

	[Test]
	public async Task Config_flow_reports_a_registry_it_cannot_write()
	{
		var flow = Flow(new LockedRegistry(RunValue), new MemoryIntent(), new KeystoneDigital.RazerController.Tests.Autostart.FakeProcess(SynapseProcessState.NotRunning));

		var result = await flow.SubmitAsync("inspector", new Dictionary<string, object?> { ["enabled"] = true }, null!, default);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	[TestCase("app.exe --inspect=127.0.0.1:9229x", false)]
	[TestCase("app.exe --inspect=127.0.0.1:9229", true)]
	[TestCase("app.exe --a --inspect=127.0.0.1:9229 --b", true)]
	public void Has_flag_matches_whole_tokens_only(string commandLine, bool expected) =>
		Assert.That(AutostartCommandLine.HasFlag(commandLine, "--inspect=127.0.0.1:9229"), Is.EqualTo(expected));

	[Test]
	public void Remove_flag_keeps_the_rest_intact() =>
		Assert.That(AutostartCommandLine.RemoveFlag("\"C:\\a b\\x.exe\" --a --inspect=127.0.0.1:9229 --b", "--inspect=127.0.0.1:9229"), Is.EqualTo("\"C:\\a b\\x.exe\" --a --b"));
}

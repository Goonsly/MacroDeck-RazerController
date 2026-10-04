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

	private static InspectorConfigFlow Flow(FakeAutostartRegistry registry) =>
		new(registry, SynapseServiceTests.CreateService(FakeInspector.FromFixture()));

	[Test]
	public async Task Config_flow_offers_the_current_flag_state()
	{
		var result = await Flow(new FakeAutostartRegistry(RunValue + " --inspect=127.0.0.1:9229")).StartAsync(null!, default);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
		Assert.That(result.NextStep!.Fields.Single().DefaultValue, Is.EqualTo(true));
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

	[TestCase("app.exe --inspect=127.0.0.1:9229x", false)]
	[TestCase("app.exe --inspect=127.0.0.1:9229", true)]
	[TestCase("app.exe --a --inspect=127.0.0.1:9229 --b", true)]
	public void Has_flag_matches_whole_tokens_only(string commandLine, bool expected) =>
		Assert.That(AutostartCommandLine.HasFlag(commandLine, "--inspect=127.0.0.1:9229"), Is.EqualTo(expected));

	[Test]
	public void Remove_flag_keeps_the_rest_intact() =>
		Assert.That(AutostartCommandLine.RemoveFlag("\"C:\\a b\\x.exe\" --a --inspect=127.0.0.1:9229 --b", "--inspect=127.0.0.1:9229"), Is.EqualTo("\"C:\\a b\\x.exe\" --a --b"));
}

using KeystoneDigital.RazerController.Synapse;
using NUnit.Framework;
using Serilog;

namespace KeystoneDigital.RazerController.Tests.Synapse;

[TestFixture]
public sealed class SynapseServiceTests
{
	internal static readonly SynapseServiceOptions Fast = new(
		TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50));

	internal static readonly ILogger Log = Serilog.Core.Logger.None;

	internal static SynapseService CreateService(FakeInspector fake) => new(fake, Log, Fast);

	[Test]
	public async Task Without_the_users_consent_the_inspector_is_never_contacted()
	{
		var fake = FakeInspector.FromFixture();
		var service = new SynapseService(fake, Log, Fast, controlEnabled: () => false);

		await service.RefreshAsync(default);

		Assert.That(service.Status, Is.EqualTo(SynapseStatus.ControlOff));
		Assert.That(fake.ConnectCount, Is.Zero);
		Assert.That(service.Devices, Is.Empty);
	}

	[Test]
	public async Task Turning_control_off_drops_the_connection()
	{
		var fake = FakeInspector.FromFixture();
		var enabled = true;
		var service = new SynapseService(fake, Log, Fast, controlEnabled: () => enabled);
		await service.RefreshAsync(default);

		enabled = false;
		await service.RefreshAsync(default);

		Assert.That(service.Status, Is.EqualTo(SynapseStatus.ControlOff));
		Assert.That(fake.IsConnected, Is.False);
		Assert.That(service.IsReachable, Is.False);
	}

	[Test]
	public async Task Refresh_publishes_devices_and_ready()
	{
		var service = CreateService(FakeInspector.FromFixture());

		await service.RefreshAsync(default);

		Assert.That(service.Status, Is.EqualTo(SynapseStatus.Ready));
		Assert.That(service.Devices, Has.Count.EqualTo(2));
		Assert.That(service.FirstOf(DeviceCategory.Keyboard)?.Name, Is.EqualTo("Razer Cynosa Chroma"));
		Assert.That(service.FindBySerial(FakeInspector.MouseSerial)?.ProductId, Is.EqualTo(103));
	}

	[Test]
	public async Task Applied_when_the_catalog_confirms_the_change()
	{
		var fake = FakeInspector.FromFixture();
		var target = fake.ProfileGuid(103, 1);
		fake.OnCommand = _ => fake.SetActiveProfile(103, target);
		var service = CreateService(fake);
		await service.RefreshAsync(default);

		var outcome = await service.SendAndConfirmAsync(FakeInspector.MouseSerial, "ON_SWITCH_PROFILE",
			new { newActiveProfileGUID = target }, d => d.ActiveProfileGuid == target, default);

		Assert.That(outcome, Is.EqualTo(CommandOutcome.Applied));
		var sent = fake.Commands.Single();
		Assert.That(sent.Type, Is.EqualTo("ON_SWITCH_PROFILE"));
		Assert.That(sent.Channel, Does.StartWith("usb_5426_103_"));
		Assert.That(sent.Payload.GetProperty("newActiveProfileGUID").GetString(), Is.EqualTo(target));
		Assert.That(service.FindBySerial(FakeInspector.MouseSerial)!.ActiveProfileGuid, Is.EqualTo(target));
	}

	[Test]
	public async Task NotApplied_when_Synapse_never_confirms()
	{
		var fake = FakeInspector.FromFixture();
		var service = CreateService(fake);
		await service.RefreshAsync(default);

		var outcome = await service.SendAndConfirmAsync(FakeInspector.MouseSerial, "ON_SWITCH_PROFILE",
			new { newActiveProfileGUID = "nope" }, d => d.ActiveProfileGuid == "nope", default);

		Assert.That(outcome, Is.EqualTo(CommandOutcome.NotApplied));
		Assert.That(fake.Commands, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task NotApplied_when_no_sender_renderer_exists()
	{
		var fake = FakeInspector.FromFixture();
		fake.CommandSent = false;
		var service = CreateService(fake);
		await service.RefreshAsync(default);

		var outcome = await service.SendAndConfirmAsync(FakeInspector.MouseSerial, "X", new { }, _ => true, default);

		Assert.That(outcome, Is.EqualTo(CommandOutcome.NotApplied));
	}

	[Test]
	public async Task NotConnected_when_Synapse_drops_while_confirming()
	{
		var fake = FakeInspector.FromFixture();
		fake.OnCommand = _ => fake.EvaluateError = new SynapseInspectorException("transport_lost", "gone");
		var service = CreateService(fake);
		await service.RefreshAsync(default);

		var outcome = await service.SendAndConfirmAsync(FakeInspector.MouseSerial, "X", new { }, _ => false, default);

		Assert.That(outcome, Is.EqualTo(CommandOutcome.NotConnected));
	}

	[Test]
	public async Task DeviceNotFound_for_an_unknown_serial()
	{
		var fake = FakeInspector.FromFixture();
		var service = CreateService(fake);
		await service.RefreshAsync(default);

		var outcome = await service.SendAndConfirmAsync("SERIAL-404", "ON_SWITCH_PROFILE", new { }, _ => true, default);

		Assert.That(outcome, Is.EqualTo(CommandOutcome.DeviceNotFound));
		Assert.That(fake.Commands, Is.Empty);
	}

	[Test]
	public async Task Unavailable_when_the_inspector_cannot_connect()
	{
		var fake = FakeInspector.FromFixture();
		fake.ConnectError = new SynapseInspectorException("inspector_unreachable", "down");
		var service = CreateService(fake);

		await service.RefreshAsync(default);
		var outcome = await service.SendAndConfirmAsync(FakeInspector.MouseSerial, "X", new { }, _ => true, default);

		Assert.That(service.Status, Is.EqualTo(SynapseStatus.InspectorUnavailable));
		Assert.That(outcome, Is.EqualTo(CommandOutcome.NotConnected));
	}

	[Test]
	public async Task Untrusted_when_the_probe_fails()
	{
		var fake = FakeInspector.FromFixture();
		fake.ConnectError = new SynapseInspectorException("incompatible_synapse", "not razer");
		var service = CreateService(fake);

		await service.RefreshAsync(default);

		Assert.That(service.Status, Is.EqualTo(SynapseStatus.InspectorUntrusted));
	}

	[Test]
	public async Task Unrecognised_when_no_device_windows_exist()
	{
		var fake = FakeInspector.FromFixture();
		fake.ClearWindows();
		var service = CreateService(fake);

		await service.RefreshAsync(default);

		Assert.That(service.Status, Is.EqualTo(SynapseStatus.SynapseUnrecognised));
	}

	[Test]
	public async Task Reconnects_after_the_inspector_drops()
	{
		var fake = FakeInspector.FromFixture();
		var service = CreateService(fake);
		await service.RefreshAsync(default);

		fake.EvaluateError = new SynapseInspectorException("transport_lost", "gone");
		await service.RefreshAsync(default);
		var whileDown = service.Status;

		fake.EvaluateError = null;
		await service.RefreshAsync(default);

		Assert.That(whileDown, Is.EqualTo(SynapseStatus.InspectorUnavailable));
		Assert.That(service.Status, Is.EqualTo(SynapseStatus.Ready));
		Assert.That(fake.ConnectCount, Is.EqualTo(2));
	}

	[Test]
	public async Task Devices_keep_their_last_values_while_unavailable()
	{
		var fake = FakeInspector.FromFixture();
		var service = CreateService(fake);
		await service.RefreshAsync(default);

		fake.EvaluateError = new SynapseInspectorException("transport_lost", "gone");
		await service.RefreshAsync(default);

		Assert.That(service.Devices, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task The_background_loop_reaches_ready()
	{
		var service = CreateService(FakeInspector.FromFixture());
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		await service.StartAsync(cts.Token);
		while (service.Status != SynapseStatus.Ready && !cts.IsCancellationRequested) await Task.Delay(10, cts.Token);
		await service.StopAsync(default);

		Assert.That(service.Status, Is.EqualTo(SynapseStatus.Ready));
	}
}

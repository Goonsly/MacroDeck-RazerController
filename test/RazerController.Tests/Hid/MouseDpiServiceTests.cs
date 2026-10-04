using KeystoneDigital.RazerController.Hid;
using KeystoneDigital.RazerController.Tests.Synapse;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Hid;

[TestFixture]
public sealed class MouseDpiServiceTests
{
	internal static MouseDpiService ServiceFor(SimulatedMouse? mouse) =>
		new(() => mouse is null ? [] : [new RazerMouseControl(new RazerHidDevice(mouse.CreateTransport(), replyDelayMs: 0))], SynapseServiceTests.Log);

	[Test]
	public async Task Refresh_reports_dpi_and_stage()
	{
		var mouse = new SimulatedMouse(800, 1600, 3200) { Dpi = 1600 };
		using var service = ServiceFor(mouse);

		await service.RefreshAsync(default);

		Assert.That(service.State, Is.EqualTo(new MouseDpiState(1600, 2, 3)));
	}

	[Test]
	public async Task Stepping_up_changes_the_stage_and_state_follows()
	{
		var mouse = new SimulatedMouse(800, 1600, 3200);
		using var service = ServiceFor(mouse);

		var outcome = await service.StepAsync(up: true, default);

		Assert.That(outcome, Is.EqualTo(DpiOutcome.Applied));
		Assert.That(mouse.Dpi, Is.EqualTo(1600));
		Assert.That(service.State, Is.EqualTo(new MouseDpiState(1600, 2, 3)));
	}

	[Test]
	public async Task Stepping_at_the_top_is_a_successful_no_op()
	{
		var mouse = new SimulatedMouse(800, 1600, 3200) { Dpi = 3200 };
		using var service = ServiceFor(mouse);

		var outcome = await service.StepAsync(up: true, default);

		Assert.That(outcome, Is.EqualTo(DpiOutcome.Applied));
		Assert.That(mouse.Dpi, Is.EqualTo(3200));
	}

	[Test]
	public async Task Temporary_dpi_is_applied_and_reported_off_stage()
	{
		var mouse = new SimulatedMouse(800, 1600, 3200);
		using var service = ServiceFor(mouse);

		var outcome = await service.SetTemporaryAsync(1000, default);

		Assert.That(outcome, Is.EqualTo(DpiOutcome.Applied));
		Assert.That(mouse.Dpi, Is.EqualTo(1000));
		Assert.That(service.State, Is.EqualTo(new MouseDpiState(1000, 0, 3)));
	}

	[Test]
	public async Task Temporary_dpi_is_clamped()
	{
		var mouse = new SimulatedMouse(800, 1600, 3200);
		using var service = ServiceFor(mouse);

		await service.SetTemporaryAsync(5, default);

		Assert.That(mouse.Dpi, Is.EqualTo(100));
	}

	[Test]
	public async Task No_mouse_reports_NoMouse()
	{
		using var service = ServiceFor(null);

		Assert.That(await service.StepAsync(up: true, default), Is.EqualTo(DpiOutcome.NoMouse));
		Assert.That(await service.SetTemporaryAsync(800, default), Is.EqualTo(DpiOutcome.NoMouse));
		Assert.That(service.State, Is.Null);
	}

	[Test]
	public async Task A_rejected_write_reports_Failed()
	{
		var mouse = new SimulatedMouse(800, 1600, 3200) { RejectWrites = true };
		using var service = ServiceFor(mouse);

		Assert.That(await service.StepAsync(up: true, default), Is.EqualTo(DpiOutcome.Failed));
		Assert.That(await service.SetTemporaryAsync(1000, default), Is.EqualTo(DpiOutcome.Failed));
	}
}

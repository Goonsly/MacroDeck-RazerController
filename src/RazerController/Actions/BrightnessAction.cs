using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

/// <summary>Synapse's per-profile lighting brightness: set a value, or step it up or down.</summary>
internal sealed class BrightnessAction(SynapseService synapse) : IDynamicOptionsActionDefinition
{
	private const string ModeParameter = "mode";
	private const string ValueParameter = "value";
	private const string StepParameter = "step";

	public string Id => "synapse-brightness";

	public LocalizedText Name => Strings.Actions.Brightness.Name();

	public LocalizedText Description => Strings.Actions.Brightness.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DeviceOptions.DeviceChoice(),
		ActionParameter.Choice(
			ModeParameter,
			[
				new ActionParameterOption { Value = "set", Label = Strings.Actions.Brightness.Mode.Set() },
				new ActionParameterOption { Value = "up", Label = Strings.Actions.Brightness.Mode.Up() },
				new ActionParameterOption { Value = "down", Label = Strings.Actions.Brightness.Mode.Down() },
			],
			label: Strings.Actions.Brightness.Mode.Label(),
			defaultValue: "set",
			required: true),
		ActionParameter.Slider(ValueParameter, 0, 100, label: Strings.Actions.Brightness.Value.Label(), step: 1, defaultValue: 100)
			.OnlyWhen(ModeParameter, "set"),
		ActionParameter.Slider(StepParameter, 1, 50, label: Strings.Actions.Brightness.Step.Label(), step: 1, defaultValue: 10)
			.OnlyWhen(ModeParameter, "up", "down"),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(synapse);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken) =>
		Task.FromResult(DeviceOptions.Devices(synapse, context, device => device.Brightness is not null));

	/// <summary>The brightness to set, worked out from the device's state when the command runs.</summary>
	internal static int Target(int current, string mode, int value, int step) => mode switch
	{
		"up" => Math.Min(100, current + step),
		"down" => Math.Max(0, current - step),
		_ => Math.Clamp(value, 0, 100),
	};

	private sealed class Executor(SynapseService synapse) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var (device, failure) = DeviceOptions.Resolve(synapse, context.Parameters);
			if (device is null) return failure!;
			if (device.Brightness is null) return ActionResult.Failed(ActionErrorCodes.ProviderRejected, Strings.Actions.Errors.Unsupported());

			var mode = ParameterReader.String(context.Parameters, ModeParameter) ?? "set";
			var value = ParameterReader.Int(context.Parameters, ValueParameter) ?? 100;
			var step = Math.Clamp(ParameterReader.Int(context.Parameters, StepParameter) ?? 10, 1, 50);

			var outcome = await synapse.SendAndConfirmAsync(device.Serial, current =>
			{
				if (current.Brightness is not { } now) return DevicePlan.Invalid;

				var target = Target(now, mode, value, step);
				if (target == now) return DevicePlan.NoChange;

				return DevicePlan.Send("ON_SET_BRIGHTNESS", new { brightness = new { isEnabled = target > 0, value = target } }, latest => latest.Brightness == target);
			}, context.CancellationToken).ConfigureAwait(false);

			return DeviceOptions.ToResult(outcome);
		}
	}
}

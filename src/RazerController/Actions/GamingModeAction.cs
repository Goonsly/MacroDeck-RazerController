using System.Text.Json;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

internal sealed class GamingModeAction(SynapseService synapse) : IDynamicOptionsActionDefinition
{
	private const string StateParameter = "state";

	public string Id => "gaming-mode";

	public LocalizedText Name => Strings.Actions.GamingMode.Name();

	public LocalizedText Description => Strings.Actions.GamingMode.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DeviceOptions.DeviceChoice(),
		ActionParameter.Choice(
			StateParameter,
			[
				new ActionParameterOption { Value = "toggle", Label = Strings.Actions.GamingMode.State.Toggle() },
				new ActionParameterOption { Value = "on", Label = Strings.Actions.GamingMode.State.On() },
				new ActionParameterOption { Value = "off", Label = Strings.Actions.GamingMode.State.Off() },
			],
			label: Strings.Actions.GamingMode.State.Label(),
			defaultValue: "toggle",
			required: true),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(synapse);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken) =>
		Task.FromResult(DeviceOptions.Devices(synapse, context, device => device.GamingMode is not null));

	/// <summary>The profile's gaming-mode object with only the on/off fields changed.</summary>
	internal static Dictionary<string, JsonElement> WithState(GamingModeState current, bool on)
	{
		var mode = current.Raw.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
		mode["state"] = JsonSerializer.SerializeToElement(on ? 1 : 0);
		mode["isWindowsKeyDisabled"] = JsonSerializer.SerializeToElement(on);
		return mode;
	}

	private sealed class Executor(SynapseService synapse) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var (device, failure) = DeviceOptions.Resolve(synapse, context.Parameters);
			if (device is null) return failure!;

			if (device.GamingMode is null)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderRejected, Strings.Actions.Errors.Unsupported());
			}

			var state = ParameterReader.String(context.Parameters, StateParameter) ?? "toggle";
			if (state is not ("on" or "off" or "toggle")) return DeviceOptions.Invalid(Strings.Actions.GamingMode.State.Label());

			var outcome = await synapse.SendAndConfirmAsync(device.Serial, fresh =>
			{
				if (fresh.GamingMode is not { } current) return DevicePlan.Invalid;

				var on = state switch
				{
					"on" => true,
					"off" => false,
					_ => !current.On,
				};

				return on == current.On
					? DevicePlan.NoChange
					: DevicePlan.Send("ON_SET_GAME_MODE", new { gamingMode = WithState(current, on) }, latest => latest.GamingMode?.On == on);
			}, context.CancellationToken).ConfigureAwait(false);

			return DeviceOptions.ToResult(outcome, () => ActionResult.Failed(ActionErrorCodes.ProviderRejected, Strings.Actions.Errors.Unsupported()));
		}
	}
}

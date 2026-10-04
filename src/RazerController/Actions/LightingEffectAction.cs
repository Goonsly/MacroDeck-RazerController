using System.Globalization;
using System.Text.Json.Serialization;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

internal sealed class LightingEffectAction(SynapseService synapse) : IDynamicOptionsActionDefinition
{
	private const string EffectParameter = "effect";
	private const string ColorParameter = "color";
	private const int StaticEffect = 1;

	/// <summary>
	/// Synapse's quick-effect ids. A device's own supported list is not reachable from outside its
	/// window, so an effect a device refuses surfaces as "not applied" through confirmation.
	/// </summary>
	internal static readonly IReadOnlyList<(int Id, Func<LocalizedText> Name)> Effects =
	[
		(0, () => Strings.Actions.LightingEffect.Effects.Off()),
		(1, () => Strings.Actions.LightingEffect.Effects.Static()),
		(2, () => Strings.Actions.LightingEffect.Effects.Breathing()),
		(3, () => Strings.Actions.LightingEffect.Effects.Spectrum()),
		(4, () => Strings.Actions.LightingEffect.Effects.Wave()),
		(5, () => Strings.Actions.LightingEffect.Effects.Reactive()),
		(6, () => Strings.Actions.LightingEffect.Effects.Ripple()),
		(7, () => Strings.Actions.LightingEffect.Effects.Starlight()),
	];

	public string Id => "lighting-effect";

	public LocalizedText Name => Strings.Actions.LightingEffect.Name();

	public LocalizedText Description => Strings.Actions.LightingEffect.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DeviceOptions.DeviceChoice(),
		ActionParameter.Choice(
			EffectParameter,
			[.. Effects.Select(effect => new ActionParameterOption { Value = effect.Id.ToString(CultureInfo.InvariantCulture), Label = effect.Name() })],
			label: Strings.Actions.LightingEffect.Effect.Label(),
			defaultValue: "1",
			required: true),
		ActionParameter.Color(ColorParameter, label: Strings.Actions.LightingEffect.Color.Label(), defaultValue: "#00FF00")
			.OnlyWhen(EffectParameter, "1"),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(synapse);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken) =>
		Task.FromResult(DeviceOptions.Devices(synapse, context, device => device.Effect is not null));

	private sealed record EffectSetting(
		[property: JsonPropertyName("selectedEffectId")] int SelectedEffectId,
		[property: JsonPropertyName("selectedEffectSetting"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ColorSetting? SelectedEffectSetting);

	private sealed record ColorSetting([property: JsonPropertyName("color1")] string Color1);

	private sealed class Executor(SynapseService synapse) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var (device, failure) = DeviceOptions.Resolve(synapse, context.Parameters);
			if (device is null) return failure!;

			var effect = ParameterReader.Int(context.Parameters, EffectParameter);
			if (effect is not { } id || Effects.All(e => e.Id != id))
			{
				return DeviceOptions.Invalid(Strings.Actions.LightingEffect.Effect.Label());
			}

			if (device.Effect is null)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderRejected, Strings.Actions.Errors.Unsupported());
			}

			var color = id == StaticEffect ? ParameterReader.String(context.Parameters, ColorParameter) ?? "#00FF00" : null;

			var outcome = await synapse.SendAndConfirmAsync(
				device.Serial,
				"ON_SET_EFFECTS",
				new { effectSetting = new EffectSetting(id, color is null ? null : new ColorSetting(color)) },
				current => current.Effect?.Id == id && (color is null || string.Equals(current.Effect.Color, color, StringComparison.OrdinalIgnoreCase)),
				context.CancellationToken).ConfigureAwait(false);

			return DeviceOptions.ToResult(outcome);
		}
	}
}

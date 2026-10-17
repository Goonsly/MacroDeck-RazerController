using KeystoneDigital.RazerController.Chroma;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

/// <summary>
/// Lighting through the Razer Chroma SDK, which needs no Synapse control. The effect is a Chroma overlay: it
/// lasts while Macro Deck runs, and "Synapse lighting" hands the devices back to the profile.
/// </summary>
internal sealed class ChromaLightingAction(ChromaClient chroma) : IActionDefinition
{
	private const string DevicesParameter = "devices";
	private const string EffectParameter = "effect";
	private const string ColorParameter = "color";
	private const string Color2Parameter = "color2";
	private const string BrightnessParameter = "brightness";
	private const string SpeedParameter = "speed";
	private const string ReverseParameter = "reverse";
	private const string AllDevices = "all";

	public string Id => "chroma-lighting";

	public LocalizedText Name => Strings.Actions.ChromaLighting.Name();

	public LocalizedText Description => Strings.Actions.ChromaLighting.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(
			DevicesParameter,
			[
				new ActionParameterOption { Value = AllDevices, Label = Strings.Actions.ChromaLighting.Devices.All() },
				new ActionParameterOption { Value = "keyboard", Label = Strings.Actions.ChromaLighting.Devices.Keyboard() },
				new ActionParameterOption { Value = "mouse", Label = Strings.Actions.ChromaLighting.Devices.Mouse() },
				new ActionParameterOption { Value = "mousepad", Label = Strings.Actions.ChromaLighting.Devices.Mousepad() },
				new ActionParameterOption { Value = "headset", Label = Strings.Actions.ChromaLighting.Devices.Headset() },
				new ActionParameterOption { Value = "keypad", Label = Strings.Actions.ChromaLighting.Devices.Keypad() },
				new ActionParameterOption { Value = "chromalink", Label = Strings.Actions.ChromaLighting.Devices.ChromaLink() },
			],
			label: Strings.Actions.ChromaLighting.Devices.Label(),
			defaultValue: AllDevices,
			required: true),
		ActionParameter.Choice(
			EffectParameter,
			[
				new ActionParameterOption { Value = "static", Label = Strings.Actions.ChromaLighting.Effects.Static() },
				new ActionParameterOption { Value = "breathing", Label = Strings.Actions.ChromaLighting.Effects.Breathing() },
				new ActionParameterOption { Value = "spectrum", Label = Strings.Actions.ChromaLighting.Effects.Spectrum() },
				new ActionParameterOption { Value = "wave", Label = Strings.Actions.ChromaLighting.Effects.Wave() },
				new ActionParameterOption { Value = "off", Label = Strings.Actions.ChromaLighting.Effects.Off() },
				new ActionParameterOption { Value = "synapse", Label = Strings.Actions.ChromaLighting.Effects.Synapse() },
			],
			label: Strings.Actions.ChromaLighting.Effect.Label(),
			defaultValue: "static",
			required: true),
		ActionParameter.Color(ColorParameter, label: Strings.Actions.LightingEffect.Color.Label(), defaultValue: "#00FF00")
			.OnlyWhen(EffectParameter, "static", "breathing"),
		ActionParameter.Color(Color2Parameter, label: Strings.Actions.ChromaLighting.Color2.Label(), description: Strings.Actions.ChromaLighting.Color2.Description(), supportsReset: true)
			.OnlyWhen(EffectParameter, "breathing"),
		ActionParameter.Slider(BrightnessParameter, 1, 100, label: Strings.Actions.ChromaLighting.Brightness.Label(), step: 1, defaultValue: 100)
			.OnlyWhen(EffectParameter, "static", "breathing", "spectrum", "wave"),
		ActionParameter.Slider(SpeedParameter, 1, 10, label: Strings.Actions.ChromaLighting.Speed.Label(), step: 1, defaultValue: 5)
			.OnlyWhen(EffectParameter, "breathing", "spectrum", "wave"),
		ActionParameter.Toggle(ReverseParameter, label: Strings.Actions.ChromaLighting.Reverse.Label())
			.OnlyWhen(EffectParameter, "wave"),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(chroma);

	/// <summary>The lighting a button asks for, or null when a parameter is unusable.</summary>
	internal static ChromaLighting? Read(IReadOnlyDictionary<string, object> parameters, string effect)
	{
		var mode = effect switch
		{
			"off" => ChromaMode.Off,
			"static" => ChromaMode.Static,
			"breathing" => ChromaMode.Breathing,
			"spectrum" => ChromaMode.Spectrum,
			"wave" => ChromaMode.Wave,
			_ => (ChromaMode?)null,
		};
		if (mode is null) return null;

		var color = ParameterReader.String(parameters, ColorParameter) ?? "#00FF00";
		var color2 = ParameterReader.String(parameters, Color2Parameter);
		try
		{
			ChromaLighting.ParseRgb(color);
			if (color2 is not null) ChromaLighting.ParseRgb(color2);
		}
		catch (FormatException)
		{
			return null;
		}

		return new ChromaLighting(
			mode.Value,
			color,
			color2,
			Math.Clamp(ParameterReader.Int(parameters, BrightnessParameter) ?? 100, 1, 100),
			Math.Clamp(ParameterReader.Int(parameters, SpeedParameter) ?? 5, 1, 10),
			string.Equals(ParameterReader.String(parameters, ReverseParameter), "true", StringComparison.OrdinalIgnoreCase));
	}

	private sealed class Executor(ChromaClient chroma) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var target = ParameterReader.String(context.Parameters, DevicesParameter) ?? AllDevices;
			IReadOnlyList<ChromaDevice>? devices = target == AllDevices ? ChromaDevice.All : ChromaDevice.Find(target) is { } one ? [one] : null;
			if (devices is null) return DeviceOptions.Invalid(Strings.Actions.ChromaLighting.Devices.Label());

			var effect = ParameterReader.String(context.Parameters, EffectParameter) ?? "static";
			if (effect == "synapse")
			{
				await chroma.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
				return ActionResult.Success();
			}

			if (Read(context.Parameters, effect) is not { } lighting) return DeviceOptions.Invalid(Strings.Actions.ChromaLighting.Effect.Label());

			return await chroma.ApplyAsync(devices, lighting, context.CancellationToken).ConfigureAwait(false) switch
			{
				ChromaOutcome.Applied => ActionResult.Success(),
				ChromaOutcome.Rejected => ActionResult.Failed(ActionErrorCodes.ProviderRejected, Strings.Actions.Errors.ChromaRejected()),
				_ => ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.ChromaUnavailable()),
			};
		}
	}
}

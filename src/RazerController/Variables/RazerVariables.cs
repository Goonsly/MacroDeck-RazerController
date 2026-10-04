using System.Globalization;
using KeystoneDigital.RazerController.Hid;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Sdk.Variables;

namespace KeystoneDigital.RazerController.Variables;

/// <summary>
/// The plugin's variables. Names are persisted in users' widgets, so they are a public API. Every read
/// comes from the services' cached state and never waits on Synapse or the mouse.
/// </summary>
internal static class RazerVariables
{
	private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(2);

	public static IReadOnlyList<VariableDefinition> Definitions { get; } =
	[
		VariableDefinition.Eager("razer_status", VariableType.Text, refreshInterval: Refresh) with { Id = "status" },
		VariableDefinition.Eager("razer_mouse_profile", VariableType.Text, refreshInterval: Refresh) with { Id = "mouse-profile" },
		VariableDefinition.Eager("razer_mouse_dpi", VariableType.Numeric, refreshInterval: Refresh) with { Id = "mouse-dpi" },
		VariableDefinition.Eager("razer_mouse_dpi_stage", VariableType.Text, refreshInterval: Refresh) with { Id = "mouse-dpi-stage" },
		VariableDefinition.Eager("razer_keyboard_profile", VariableType.Text, refreshInterval: Refresh) with { Id = "keyboard-profile" },
		VariableDefinition.Eager("razer_keyboard_effect", VariableType.Text, refreshInterval: Refresh) with { Id = "keyboard-effect" },
		VariableDefinition.Eager("razer_keyboard_gaming_mode", VariableType.Boolean, refreshInterval: Refresh) with { Id = "keyboard-gaming-mode" },
	];

	public static VariableReading Read(string localId, SynapseService synapse, MouseDpiService dpi)
	{
		var usable = synapse.Status == SynapseStatus.Ready;
		var mouse = usable ? synapse.FirstOf(DeviceCategory.Mouse) : null;
		var keyboard = usable ? synapse.FirstOf(DeviceCategory.Keyboard) : null;
		var dpiState = dpi.State;

		object? value = localId switch
		{
			"status" => synapse.Status.ToVariableValue(),
			"mouse-profile" => mouse?.ActiveProfileName,
			"mouse-dpi" => dpiState?.Dpi,
			"mouse-dpi-stage" => dpiState is null ? null : string.Create(CultureInfo.InvariantCulture, $"{dpiState.StageIndex}/{dpiState.StageCount}"),
			"keyboard-profile" => keyboard?.ActiveProfileName,
			"keyboard-effect" => keyboard?.Effect is { } effect ? EffectName(effect.Id) : null,
			"keyboard-gaming-mode" => keyboard?.GamingMode?.On,
			_ => null,
		};

		return value is null ? VariableReading.Unavailable : VariableReading.Of(value);
	}

	// A variable value is data that widgets compare against, so it stays a stable English token rather
	// than following the UI language.
	private static string EffectName(int id) => id switch
	{
		0 => "Off",
		1 => "Static",
		2 => "Breathing",
		3 => "Spectrum",
		4 => "Wave",
		5 => "Reactive",
		6 => "Ripple",
		7 => "Starlight",
		_ => id.ToString(CultureInfo.InvariantCulture),
	};
}

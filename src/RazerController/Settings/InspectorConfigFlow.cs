using System.Text.Json;
using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace KeystoneDigital.RazerController.Settings;

/// <summary>
/// The one setting: whether Synapse's HKCU startup entry carries the inspector flag. The flow writes
/// the registry itself because the flag lives there, not in Macro Deck's config.
/// </summary>
internal sealed class InspectorConfigFlow(IAutostartRegistry registry, SynapseService synapse, ISynapseControlIntent intent) : IConfigFlow
{
	public const string InspectFlag = "--inspect=127.0.0.1:9229";

	private const string StepId = "inspector";
	private const string EnabledField = "enabled";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
		Task.FromResult(ConfigFlowResult.Step(BuildStep()));

	public Task<ConfigFlowResult> SubmitAsync(string stepId, IReadOnlyDictionary<string, object?> input, IConfigFlowContext context, CancellationToken cancellationToken)
	{
		var current = registry.GetValue();
		if (string.IsNullOrWhiteSpace(current))
		{
			return Task.FromResult(ConfigFlowResult.Error(BuildStep(), Strings.Settings.Inspector.NotInstalled(), new Dictionary<string, MacroDeck.Localization.LocalizedText>()));
		}

		var enable = ReadBool(input, EnabledField);
		intent.Set(enable);
		var updated = enable ? AutostartCommandLine.AppendFlag(current, InspectFlag) : AutostartCommandLine.RemoveFlag(current, InspectFlag);
		if (updated != current) registry.SetValue(updated);

		return Task.FromResult(ConfigFlowResult.Complete(
			"Synapse Control for Razer",
			new Dictionary<string, ConfigFlowValue> { [EnabledField] = ConfigFlowValue.Plain(enable ? "true" : "false") }));
	}

	private ConfigFlowStep BuildStep()
	{
		var enabled = registry.GetValue() is { } value && AutostartCommandLine.HasFlag(value, InspectFlag);

		return new ConfigFlowStep
		{
			StepId = StepId,
			Title = Strings.Settings.Inspector.Title(),
			Description = Strings.Settings.Inspector.Description(
				enabled ? Strings.Settings.Inspector.On() : Strings.Settings.Inspector.Off(),
				synapse.Status.ToVariableValue()),
			Instructions = [new ConfigFlowInstruction { Text = Strings.Settings.Inspector.SecurityWarning() }],
			Fields = [ActionParameter.Toggle(EnabledField, label: Strings.Settings.Inspector.Enabled.Label(), defaultValue: enabled)],
		};
	}

	private static bool ReadBool(IReadOnlyDictionary<string, object?> input, string name) =>
		input.TryGetValue(name, out var value) && value switch
		{
			bool flag => flag,
			JsonElement { ValueKind: JsonValueKind.True } => true,
			JsonElement { ValueKind: JsonValueKind.String } json => bool.TryParse(json.GetString(), out var parsed) && parsed,
			string text => bool.TryParse(text, out var parsed) && parsed,
			_ => false,
		};
}

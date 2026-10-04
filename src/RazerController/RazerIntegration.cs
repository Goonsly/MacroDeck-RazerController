using KeystoneDigital.RazerController.Actions;
using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Hid;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using KeystoneDigital.RazerController.Settings;
using KeystoneDigital.RazerController.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace KeystoneDigital.RazerController;

public sealed class RazerIntegration : IPluginIntegration, IVariableProvider, IConfigFlowProvider
{
	private readonly MouseDpiService _dpi;

	internal RazerIntegration(SynapseService synapse, MouseDpiService dpi, IAutostartRegistry autostart)
	{
		_dpi = dpi;
		Synapse = synapse;
		Autostart = autostart;
		Actions =
		[
			new SwitchProfileAction(synapse),
			new LightingEffectAction(synapse),
			new GamingModeAction(synapse),
			new StepDpiAction(dpi),
			new TemporaryDpiAction(dpi),
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	internal SynapseService Synapse { get; }

	internal IAutostartRegistry Autostart { get; }

	public static RazerIntegration Create(IServiceProvider services) => new(
		services.GetRequiredService<SynapseService>(),
		services.GetRequiredService<MouseDpiService>(),
		services.GetRequiredService<IAutostartRegistry>());

	public Task InitializeAsync(IIntegrationContext context) => _dpi.RefreshAsync(CancellationToken.None);

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<VariableDefinition> Variables => RazerVariables.Definitions;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(RazerVariables.Read(localId, Synapse, _dpi));

	public bool RequiresConfiguration => false;

	public bool AllowsMultipleConfigurations => false;

	public IConfigFlow CreateConfigFlow() => new InspectorConfigFlow(Autostart, Synapse);
}

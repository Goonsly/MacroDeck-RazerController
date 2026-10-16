using KeystoneDigital.RazerController.Settings;
using KeystoneDigital.RazerController.Synapse;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KeystoneDigital.RazerController.Autostart;

/// <summary>
/// Synapse rewrites its own Windows startup entry and drops the inspector flag. While the user has
/// Synapse control on, this puts the flag back and restarts a Synapse that came up without it.
/// </summary>
internal sealed class InspectorGuard(
	IAutostartRegistry registry, ISynapseControlIntent intent, ISynapseProcess process, ILogger logger, TimeProvider clock)
{
	/// <summary>Keeps a Synapse that refuses the flag from being restarted in a loop.</summary>
	public static readonly TimeSpan RestartCooldown = TimeSpan.FromMinutes(10);

	private readonly ILogger _logger = logger.ForContext<InspectorGuard>();
	private DateTimeOffset? _lastRestart;

	public async Task EnsureAsync(SynapseStatus status, CancellationToken cancellationToken)
	{
		if (status == SynapseStatus.Ready)
		{
			// A working connection means someone deliberately set the flag; remember that as consent.
			if (intent.Get() is null) intent.Set(true);
			return;
		}

		if (status != SynapseStatus.InspectorUnavailable || intent.Get() != true) return;

		var commandLine = registry.GetValue();
		if (string.IsNullOrWhiteSpace(commandLine)) return;

		if (!AutostartCommandLine.HasFlag(commandLine, InspectorConfigFlow.InspectFlag))
		{
			commandLine = AutostartCommandLine.AppendFlag(commandLine, InspectorConfigFlow.InspectFlag);
			registry.SetValue(commandLine);
			_logger.Warning("Synapse removed the inspector flag from its startup entry; put it back");
		}

		if (process.GetState() != SynapseProcessState.RunningWithoutFlag) return;

		var now = clock.GetUtcNow();
		if (_lastRestart is { } last && now - last < RestartCooldown) return;

		_lastRestart = now;
		_logger.Warning("Synapse is running without the inspector flag; restarting it");
		await process.RestartAsync(commandLine, cancellationToken).ConfigureAwait(false);
	}
}

internal sealed class InspectorGuardService(InspectorGuard guard, SynapseService synapse, ILogger logger) : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Let the first catalog refresh settle the status before judging it.
		try
		{
			await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await guard.EnsureAsync(synapse.Status, stoppingToken).ConfigureAwait(false);
				await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				logger.Error(ex, "Could not check Synapse's inspector flag");
				await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
			}
		}
	}
}

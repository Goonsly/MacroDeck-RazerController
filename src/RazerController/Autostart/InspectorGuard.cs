using KeystoneDigital.RazerController.Settings;
using KeystoneDigital.RazerController.Synapse;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KeystoneDigital.RazerController.Autostart;

/// <summary>Why Synapse control is on but the inspector does not answer.</summary>
internal enum SetupIssue
{
	/// <summary>Synapse rewrote its startup entry and dropped the flag. Saving the settings again puts it back.</summary>
	FlagMissing,

	/// <summary>Synapse is running without the flag. The user restarts it, or presses the Restart Synapse action.</summary>
	RestartNeeded,
}

/// <summary>
/// Synapse rewrites its own Windows startup entry and drops the inspector flag. This only reports that:
/// it never writes the startup entry and never restarts Synapse. The settings screen and the user's
/// Restart Synapse button are the only things that change either.
/// </summary>
internal sealed class InspectorGuard(IAutostartRegistry registry, ISynapseControlIntent intent, ISynapseProcess process, ILogger logger)
{
	private readonly ILogger _logger = logger.ForContext<InspectorGuard>();
	private int _issue = -1;

	public SetupIssue? Issue => Volatile.Read(ref _issue) is var value && value >= 0 ? (SetupIssue)value : null;

	public void Check(SynapseStatus status)
	{
		var issue = Evaluate(status);
		var next = issue is { } found ? (int)found : -1;
		if (Interlocked.Exchange(ref _issue, next) != next && issue is not null)
		{
			_logger.Warning("Synapse control is on but Synapse is not reachable: {Issue}", issue);
		}
	}

	private SetupIssue? Evaluate(SynapseStatus status)
	{
		if (status != SynapseStatus.InspectorUnavailable || intent.Get() != true) return null;

		var commandLine = registry.GetValue();
		if (string.IsNullOrWhiteSpace(commandLine)) return null;

		if (!AutostartCommandLine.HasFlag(commandLine, InspectorConfigFlow.InspectFlag)) return SetupIssue.FlagMissing;

		return process.GetState() == SynapseProcessState.RunningWithoutFlag ? SetupIssue.RestartNeeded : null;
	}
}

internal sealed class InspectorGuardService(InspectorGuard guard, SynapseService synapse, ILogger logger) : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

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
				guard.Check(synapse.Status);
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

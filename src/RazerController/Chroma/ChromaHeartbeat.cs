using Microsoft.Extensions.Hosting;
using Serilog;

namespace KeystoneDigital.RazerController.Chroma;

/// <summary>
/// Draws animation frames, and keeps the Chroma session alive: the SDK closes a session that stays quiet for
/// 15 seconds. Stopping the plugin ends the session, so lighting goes back to Synapse instead of staying on
/// the last Chroma frame.
/// </summary>
internal sealed class ChromaHeartbeat(ChromaClient chroma, ILogger logger, TimeSpan? frame = null, TimeSpan? heartbeat = null) : BackgroundService
{
	private readonly TimeSpan _frame = frame ?? TimeSpan.FromMilliseconds(66);
	private readonly TimeSpan _heartbeat = heartbeat ?? TimeSpan.FromSeconds(5);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var sinceHeartbeat = TimeSpan.Zero;

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(_frame, stoppingToken).ConfigureAwait(false);
				sinceHeartbeat += _frame;

				if (sinceHeartbeat >= _heartbeat)
				{
					sinceHeartbeat = TimeSpan.Zero;
					await chroma.HeartbeatAsync(stoppingToken).ConfigureAwait(false);
				}
				else if (chroma.IsAnimating)
				{
					await chroma.AnimateAsync(stoppingToken).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				logger.Error(ex, "Chroma update failed");
			}
		}
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken).ConfigureAwait(false);
		await chroma.ReleaseAsync(cancellationToken).ConfigureAwait(false);
	}
}

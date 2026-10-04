using Microsoft.Extensions.Hosting;

namespace KeystoneDigital.RazerController.Hid;

/// <summary>
/// Re-reads the mouse every interval, so the DPI variables follow the mouse's own DPI button, profile
/// switches and replugs, not only this plugin's actions.
/// </summary>
internal sealed class MouseDpiPoller(MouseDpiService dpi, TimeSpan interval) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await dpi.RefreshAsync(stoppingToken).ConfigureAwait(false);
				await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}
}

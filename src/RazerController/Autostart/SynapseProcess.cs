using System.Diagnostics;
using System.Management;
using KeystoneDigital.RazerController.Settings;

namespace KeystoneDigital.RazerController.Autostart;

internal enum SynapseProcessState
{
	NotRunning,
	RunningWithFlag,
	RunningWithoutFlag,
}

internal interface ISynapseProcess
{
	SynapseProcessState GetState();

	Task RestartAsync(string commandLine, CancellationToken cancellationToken);
}

/// <summary>
/// RazerAppEngine runs as one main process plus Chromium children started with --type=. Only the main
/// process's command line says whether the inspector flag is in effect.
/// </summary>
internal sealed class WindowsSynapseProcess : ISynapseProcess
{
	private const string ProcessName = "RazerAppEngine";

	public SynapseProcessState GetState()
	{
		using var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name = 'RazerAppEngine.exe'");
		using var results = searcher.Get();

		var main = results.Cast<ManagementBaseObject>()
			.Select(process => process["CommandLine"] as string ?? "")
			.FirstOrDefault(commandLine => !commandLine.Contains("--type=", StringComparison.Ordinal));

		if (main is null) return SynapseProcessState.NotRunning;

		return AutostartCommandLine.HasFlag(main, InspectorConfigFlow.InspectFlag)
			? SynapseProcessState.RunningWithFlag
			: SynapseProcessState.RunningWithoutFlag;
	}

	public async Task RestartAsync(string commandLine, CancellationToken cancellationToken)
	{
		foreach (var process in Process.GetProcessesByName(ProcessName))
		{
			using (process)
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
				{
					// Already exiting as a child of a process killed earlier in this loop.
				}
			}
		}

		for (var i = 0; i < 50 && Process.GetProcessesByName(ProcessName).Length > 0; i++)
		{
			await Task.Delay(100, cancellationToken).ConfigureAwait(false);
		}

		var executable = AutostartCommandLine.ExtractExecutable(commandLine)
			?? throw new InvalidOperationException("Synapse's startup entry names no executable.");

		using var started = Process.Start(new ProcessStartInfo(executable, AutostartCommandLine.ExtractArguments(commandLine))
		{
			UseShellExecute = false,
			WorkingDirectory = Path.GetDirectoryName(executable) ?? "",
		});
	}
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KeystoneDigital.RazerController.Synapse;

internal sealed record SynapseServiceOptions(TimeSpan RefreshInterval, TimeSpan ConfirmTimeout, TimeSpan ConfirmPoll, TimeSpan MaxBackoff)
{
	public static SynapseServiceOptions Default { get; } = new(
		TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
}

/// <summary>
/// Keeps a session to Synapse's inspector, a fresh device catalog, and the one path that changes
/// anything in Synapse. Synapse's stored state is the source of truth: a command counts as applied only
/// once a later catalog shows it.
/// </summary>
internal sealed class SynapseService : BackgroundService
{
	private readonly IInspector _inspector;
	private readonly ILogger _logger;
	private readonly SynapseServiceOptions _options;
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceGates = new(StringComparer.Ordinal);

	private IReadOnlyList<RazerDeviceInfo> _devices = [];
	private int _status = (int)SynapseStatus.Starting;

	public SynapseService(IInspector inspector, ILogger logger, SynapseServiceOptions? options = null)
	{
		_inspector = inspector;
		_logger = logger.ForContext<SynapseService>();
		_options = options ?? SynapseServiceOptions.Default;
	}

	public SynapseStatus Status => (SynapseStatus)Volatile.Read(ref _status);

	/// <summary>Synapse answers, even if a given device is not there; a missing device is then NotFound, not "Synapse unavailable".</summary>
	public bool IsReachable => (Status is SynapseStatus.Ready or SynapseStatus.NoDevices or SynapseStatus.SynapseUnrecognised) && _inspector.IsConnected;

	public IReadOnlyList<RazerDeviceInfo> Devices => Volatile.Read(ref _devices);

	public string? SynapseVersion { get; private set; }

	public RazerDeviceInfo? FindBySerial(string serial) => Devices.FirstOrDefault(d => d.Serial == serial);

	public RazerDeviceInfo? FirstOf(DeviceCategory category) => Devices.FirstOrDefault(d => d.Category == category);

	public async Task RefreshAsync(CancellationToken cancellationToken)
	{
		try
		{
			if (!_inspector.IsConnected) await _inspector.ConnectAsync(cancellationToken).ConfigureAwait(false);

			var snapshot = await _inspector.EvaluateAsync(InspectorScripts.Catalog, cancellationToken).ConfigureAwait(false);
			var result = SynapseCatalogParser.Parse(snapshot);

			SynapseVersion = result.SynapseVersion;
			Volatile.Write(ref _devices, result.Devices);

			SetStatus(result switch
			{
				{ WindowCount: 0 } => SynapseStatus.SynapseUnrecognised,
				{ Devices.Count: 0 } => SynapseStatus.NoDevices,
				_ => SynapseStatus.Ready,
			});
		}
		catch (SynapseInspectorException ex)
		{
			_inspector.Disconnect();
			SetStatus(ex.Code == "incompatible_synapse" ? SynapseStatus.InspectorUntrusted : SynapseStatus.InspectorUnavailable, ex.Message);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Synapse's storage is not a contract; an unexpected shape must not end the refresh loop.
			_logger.Error(ex, "Could not read Synapse's device catalog");
			SetStatus(SynapseStatus.SynapseUnrecognised, ex.Message);
		}
	}

	public async Task<CommandOutcome> SendAndConfirmAsync(
		string serial, string eventType, object payload, Func<RazerDeviceInfo, bool> isApplied, CancellationToken cancellationToken)
	{
		if (!IsReachable) return CommandOutcome.NotConnected;

		var gate = _deviceGates.GetOrAdd(serial, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (FindBySerial(serial) is not { } device) return CommandOutcome.DeviceNotFound;

			var reply = await _inspector.EvaluateAsync(InspectorScripts.Command(device.Channel, eventType, payload), cancellationToken).ConfigureAwait(false);
			var sent = reply is { ValueKind: System.Text.Json.JsonValueKind.Object } value
				&& value.TryGetProperty("sent", out var flag)
				&& flag.ValueKind == System.Text.Json.JsonValueKind.True;

			if (!sent)
			{
				_logger.Warning("Synapse had no renderer to send {EventType} from", eventType);
				return CommandOutcome.NotApplied;
			}

			var deadline = DateTime.UtcNow + _options.ConfirmTimeout;

			while (true)
			{
				await RefreshAsync(cancellationToken).ConfigureAwait(false);
				if (FindBySerial(serial) is { } current && isApplied(current)) return CommandOutcome.Applied;
				if (DateTime.UtcNow >= deadline) break;

				await Task.Delay(_options.ConfirmPoll, cancellationToken).ConfigureAwait(false);
			}

			_logger.Warning("Synapse did not confirm {EventType} on {Device} within {Timeout}", eventType, device.Name, _options.ConfirmTimeout);
			return CommandOutcome.NotApplied;
		}
		catch (SynapseInspectorException ex)
		{
			_inspector.Disconnect();
			SetStatus(SynapseStatus.InspectorUnavailable, ex.Message);
			return CommandOutcome.NotConnected;
		}
		finally
		{
			gate.Release();
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var backoff = TimeSpan.FromSeconds(1);

		while (!stoppingToken.IsCancellationRequested)
		{
			await RefreshAsync(stoppingToken).ConfigureAwait(false);

			TimeSpan delay;
			if (Status is SynapseStatus.InspectorUnavailable or SynapseStatus.InspectorUntrusted)
			{
				delay = backoff;
				backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _options.MaxBackoff.Ticks));
			}
			else
			{
				delay = _options.RefreshInterval;
				backoff = TimeSpan.FromSeconds(1);
			}

			try
			{
				await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private void SetStatus(SynapseStatus status, string? detail = null)
	{
		var previous = (SynapseStatus)Interlocked.Exchange(ref _status, (int)status);
		if (previous == status) return;

		_logger.Information("Synapse status {Previous} -> {Status} (Synapse {Version}, {DeviceCount} devices) {Detail}",
			previous, status, SynapseVersion, Devices.Count, detail ?? "");
	}
}

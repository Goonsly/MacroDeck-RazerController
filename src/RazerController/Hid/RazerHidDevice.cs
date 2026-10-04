namespace KeystoneDigital.RazerController.Hid;

/// <summary>
/// One command at a time, with the retry that makes this protocol usable while Synapse runs: Synapse
/// streams Chroma frames through the same reply slot, so a reply that does not match the command sent
/// belongs to Synapse. Blocking by design; callers run it on the thread pool.
/// </summary>
internal sealed class RazerHidDevice : IDisposable
{
	private readonly IFeatureReportTransport _transport;
	private readonly int _maxAttempts;
	private readonly int _replyDelayMs;
	private readonly Lock _gate = new();

	public RazerHidDevice(IFeatureReportTransport transport, int maxAttempts = 40, int replyDelayMs = 8)
	{
		_transport = transport;
		_maxAttempts = maxAttempts;
		_replyDelayMs = replyDelayMs;
	}

	/// <summary>Sends a 90-byte report and returns the matching 91-byte reply, or null.</summary>
	public byte[]? Exchange(byte[] report)
	{
		var buffer = new byte[91];
		Array.Copy(report, 0, buffer, 1, 90);

		lock (_gate)
		{
			for (var attempt = 0; attempt < _maxAttempts; attempt++)
			{
				if (!_transport.SetFeature(buffer)) return null;

				if (_replyDelayMs > 0) Thread.Sleep(_replyDelayMs);

				var reply = new byte[91];
				if (!_transport.GetFeature(reply)) return null;

				// The class and id sit one byte later in the reply because of the leading report id.
				if (reply[7] == report[6] && reply[8] == report[7]) return reply;
			}
		}

		return null;
	}

	public void Dispose() => _transport.Dispose();
}

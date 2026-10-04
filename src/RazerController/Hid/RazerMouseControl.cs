namespace KeystoneDigital.RazerController.Hid;

/// <summary>
/// DPI and DPI stages on one mouse. Every write is NOSTORE, so nothing here touches onboard flash. A
/// Synapse profile switch overwrites all of it, which is intended: the profile wins.
/// </summary>
internal sealed class RazerMouseControl : IDisposable
{
	private const byte DpiClass = 0x04;
	private const byte GetDpiId = 0x85;
	private const byte SetDpiId = 0x05;
	private const byte GetStagesId = 0x86;
	private const byte SetStagesId = 0x06;

	private readonly RazerHidDevice _device;

	public RazerMouseControl(RazerHidDevice device) => _device = device;

	public (int X, int Y)? ReadDpi()
	{
		var reply = _device.Exchange(RazerReport.Build(DpiClass, GetDpiId, 0x07, RazerReport.NoStore));

		if (reply is null || reply[1] != (byte)RazerStatus.Success) return null;

		return ((reply[10] << 8) | reply[11], (reply[12] << 8) | reply[13]);
	}

	public DpiStages? ReadStages()
	{
		var reply = _device.Exchange(RazerReport.Build(DpiClass, GetStagesId, DpiStages.PayloadSize, RazerReport.VarStore));

		return reply is null ? null : DpiStages.Parse(reply);
	}

	/// <summary>
	/// Moves the active stage. A NOSTORE write does not change what a VARSTORE read reports, so callers
	/// learn the effective DPI from ReadDpi.
	/// </summary>
	public bool SetActiveStage(byte stageId)
	{
		var stages = ReadStages();
		if (stages is null || stages.IndexOf(stageId) < 0) return false;

		var reply = _device.Exchange(RazerReport.Build(
			DpiClass, SetStagesId, DpiStages.PayloadSize, stages.ToPayload(stageId, RazerReport.NoStore)));

		return reply is not null && reply[1] == (byte)RazerStatus.Success;
	}

	public bool SetDpi(int x, int y)
	{
		var reply = _device.Exchange(RazerReport.Build(DpiClass, SetDpiId, 0x07,
			RazerReport.NoStore,
			(byte)(x >> 8), (byte)(x & 0xFF),
			(byte)(y >> 8), (byte)(y & 0xFF),
			0x00, 0x00));

		return reply is not null && reply[1] == (byte)RazerStatus.Success;
	}

	public void Dispose() => _device.Dispose();
}

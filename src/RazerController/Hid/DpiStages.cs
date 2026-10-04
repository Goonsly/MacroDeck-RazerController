namespace KeystoneDigital.RazerController.Hid;

internal sealed record DpiStage(byte Id, int X, int Y);

/// <summary>
/// The DPI stages of the mouse's current profile. Layout from offset 9 of the 91-byte reply: storage,
/// active stage id (1-based), stage count, then seven bytes per stage: id, X big-endian, Y big-endian, 0, 0.
/// </summary>
internal sealed record DpiStages(byte ActiveStageId, IReadOnlyList<DpiStage> Stages)
{
	public const byte PayloadSize = 0x26;

	private const int MaxStages = 5;

	public static DpiStages? Parse(byte[] reply)
	{
		if (reply.Length < 91) return null;
		if (reply[1] != (byte)RazerStatus.Success) return null;

		var active = reply[10];
		int count = reply[11];
		if (count is < 1 or > MaxStages) return null;

		var stages = new List<DpiStage>(count);
		for (var i = 0; i < count; i++)
		{
			var o = 12 + i * 7;
			stages.Add(new DpiStage(reply[o], (reply[o + 1] << 8) | reply[o + 2], (reply[o + 3] << 8) | reply[o + 4]));
		}

		return new DpiStages(active, stages);
	}

	public int IndexOf(byte stageId)
	{
		for (var i = 0; i < Stages.Count; i++)
		{
			if (Stages[i].Id == stageId) return i;
		}

		return -1;
	}

	public byte[] ToPayload(byte activeStageId, byte storage)
	{
		var payload = new byte[PayloadSize];
		payload[0] = storage;
		payload[1] = activeStageId;
		payload[2] = (byte)Stages.Count;

		for (var i = 0; i < Stages.Count; i++)
		{
			var o = 3 + i * 7;
			var stage = Stages[i];
			payload[o] = stage.Id;
			payload[o + 1] = (byte)(stage.X >> 8);
			payload[o + 2] = (byte)(stage.X & 0xFF);
			payload[o + 3] = (byte)(stage.Y >> 8);
			payload[o + 4] = (byte)(stage.Y & 0xFF);
		}

		return payload;
	}
}

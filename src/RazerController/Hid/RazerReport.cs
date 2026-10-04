namespace KeystoneDigital.RazerController.Hid;

/// <summary>
/// The Razer vendor report, as reverse-engineered by OpenRazer: 90 bytes, sent as a feature report
/// with report id 0x00 in front, so 91 bytes on the wire.
/// </summary>
internal static class RazerReport
{
	public const byte TransactionId = 0x1f;

	/// <summary>Volatile. Every repeated write uses this.</summary>
	public const byte NoStore = 0x00;

	/// <summary>Writes the mouse's onboard flash. Reads only, here.</summary>
	public const byte VarStore = 0x01;

	public static byte[] Build(byte commandClass, byte commandId, byte dataSize, params byte[] arguments)
	{
		var report = new byte[90];
		report[1] = TransactionId;
		report[5] = dataSize;
		report[6] = commandClass;
		report[7] = commandId;
		Array.Copy(arguments, 0, report, 8, arguments.Length);
		report[88] = Crc(report);
		return report;
	}

	public static byte Crc(byte[] report)
	{
		byte crc = 0;
		for (var i = 2; i < 88; i++) crc ^= report[i];
		return crc;
	}
}

internal enum RazerStatus : byte
{
	New = 0,
	Busy = 1,
	Success = 2,
	Failure = 3,
	Timeout = 4,
	NotSupported = 5,
}

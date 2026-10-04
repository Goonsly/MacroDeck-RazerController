using KeystoneDigital.RazerController.Hid;

namespace KeystoneDigital.RazerController.Tests.Hid;

/// <summary>A FakeTransport responder that behaves like the mouse's DPI command class.</summary>
internal sealed class SimulatedMouse
{
	public SimulatedMouse(params int[] stages)
	{
		Stages = stages;
		Dpi = stages[0];
	}

	public int[] Stages { get; }

	public int Dpi { get; set; }

	public bool RejectWrites { get; set; }

	public FakeTransport CreateTransport() => new() { Responder = Respond };

	private byte[] Respond(byte[] sent)
	{
		// sent is the 91-byte buffer: report id, then the 90-byte report.
		var commandClass = sent[7];
		var commandId = sent[8];
		var reply = new byte[91];
		reply[1] = (byte)RazerStatus.Success;
		reply[7] = commandClass;
		reply[8] = commandId;

		switch (commandId)
		{
			case 0x85:
				reply[10] = (byte)(Dpi >> 8);
				reply[11] = (byte)(Dpi & 0xFF);
				reply[12] = reply[10];
				reply[13] = reply[11];
				break;
			case 0x86:
				var payload = new DpiStages(1, Stages.Select((x, i) => new DpiStage((byte)(i + 1), x, x)).ToList()).ToPayload(1, RazerReport.VarStore);
				Array.Copy(payload, 0, reply, 9, payload.Length);
				break;
			case 0x06:
				if (RejectWrites) reply[1] = (byte)RazerStatus.Failure;
				else Dpi = Stages[sent[10] - 1];
				break;
			case 0x05:
				if (RejectWrites) reply[1] = (byte)RazerStatus.Failure;
				else Dpi = (sent[10] << 8) | sent[11];
				break;
		}

		return reply;
	}
}

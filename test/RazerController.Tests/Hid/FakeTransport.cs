using KeystoneDigital.RazerController.Hid;

namespace KeystoneDigital.RazerController.Tests.Hid;

internal sealed class FakeTransport : IFeatureReportTransport
{
	private readonly Queue<byte[]> _replies;

	public FakeTransport(params byte[][] replies) => _replies = new Queue<byte[]>(replies);

	public int SetCount { get; private set; }

	public List<byte[]> Sent { get; } = [];

	public bool FailSet { get; set; }

	public Func<byte[], byte[]?>? Responder { get; set; }

	public bool SetFeature(byte[] buffer)
	{
		if (FailSet) return false;
		SetCount++;
		Sent.Add((byte[])buffer.Clone());
		return true;
	}

	public bool GetFeature(byte[] buffer)
	{
		var reply = Responder?.Invoke(Sent[^1]) ?? (_replies.Count > 0 ? _replies.Dequeue() : null);
		if (reply is null) return false;
		Array.Copy(reply, buffer, Math.Min(reply.Length, buffer.Length));
		return true;
	}

	public void Dispose()
	{
	}
}

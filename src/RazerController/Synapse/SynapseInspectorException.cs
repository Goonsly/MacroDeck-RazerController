namespace KeystoneDigital.RazerController.Synapse;

internal sealed class SynapseInspectorException : Exception
{
	public SynapseInspectorException(string code, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Code = code;
	}

	public string Code { get; }
}

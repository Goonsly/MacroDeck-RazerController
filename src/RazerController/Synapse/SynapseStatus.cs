namespace KeystoneDigital.RazerController.Synapse;

internal enum SynapseStatus
{
	Starting,
	Ready,
	InspectorUnavailable,
	InspectorUntrusted,
	SynapseUnrecognised,
	NoDevices,
}

internal enum CommandOutcome
{
	Applied,
	NotConnected,
	DeviceNotFound,
	NotApplied,
}

internal static class SynapseStatusText
{
	/// <summary>The value of the razer_status variable.</summary>
	public static string ToVariableValue(this SynapseStatus status) => status switch
	{
		SynapseStatus.Starting => "starting",
		SynapseStatus.Ready => "ready",
		SynapseStatus.InspectorUnavailable => "inspector_unavailable",
		SynapseStatus.InspectorUntrusted => "inspector_untrusted",
		SynapseStatus.SynapseUnrecognised => "synapse_unrecognised",
		SynapseStatus.NoDevices => "no_devices",
		_ => "unknown",
	};
}

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
	NoChange,
	Invalid,
}

/// <summary>What to send, decided from the device's state at the moment the command runs.</summary>
internal sealed record DevicePlan(string? EventType, object? Payload, Func<RazerDeviceInfo, bool>? IsApplied, CommandOutcome? Shortcut)
{
	public static DevicePlan NoChange { get; } = new(null, null, null, CommandOutcome.NoChange);

	public static DevicePlan Invalid { get; } = new(null, null, null, CommandOutcome.Invalid);

	public static DevicePlan Send(string eventType, object payload, Func<RazerDeviceInfo, bool> isApplied) => new(eventType, payload, isApplied, null);
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

using System.Text.Json;

namespace KeystoneDigital.RazerController.Synapse;

internal enum DeviceCategory
{
	Mouse,
	Keyboard,
	Other,
}

internal sealed record SynapseProfile(string Guid, string Name);

internal sealed record LightingEffect(int Id, string? Color);

/// <summary>The active profile's gaming mode. <see cref="Raw"/> keeps every field so a write changes only what it means to.</summary>
internal sealed record GamingModeState(bool On, JsonElement Raw);

internal sealed record RazerDeviceInfo(
	string Serial,
	int ProductId,
	string Name,
	DeviceCategory Category,
	string Channel,
	IReadOnlyList<SynapseProfile> Profiles,
	string? ActiveProfileGuid,
	LightingEffect? Effect,
	GamingModeState? GamingMode)
{
	public string? ActiveProfileName => Profiles.FirstOrDefault(p => p.Guid == ActiveProfileGuid)?.Name;
}

internal sealed record CatalogParseResult(IReadOnlyList<RazerDeviceInfo> Devices, string? SynapseVersion, int WindowCount)
{
	public static CatalogParseResult Empty { get; } = new([], null, 0);
}

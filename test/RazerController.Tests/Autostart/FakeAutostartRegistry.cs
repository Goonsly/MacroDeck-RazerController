using KeystoneDigital.RazerController.Autostart;

namespace KeystoneDigital.RazerController.Tests;

internal sealed class FakeAutostartRegistry(string? value) : IAutostartRegistry
{
	public string? Value { get; private set; } = value;

	public string? GetValue() => Value;

	public void SetValue(string value) => Value = value;
}

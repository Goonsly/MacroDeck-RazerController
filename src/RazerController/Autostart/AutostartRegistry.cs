using Microsoft.Win32;

namespace KeystoneDigital.RazerController.Autostart;

internal interface IAutostartRegistry
{
	string? GetValue();

	void SetValue(string value);
}

internal sealed class WindowsAutostartRegistry : IAutostartRegistry
{
	private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "RazerAppEngine";

	private RegistryValueKind _lastKnownKind = RegistryValueKind.String;

	public string? GetValue()
	{
		using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
		if (key is null) return null;

		try
		{
			var kind = key.GetValueKind(ValueName);
			if (kind == RegistryValueKind.String || kind == RegistryValueKind.ExpandString)
			{
				_lastKnownKind = kind;
			}
		}
		catch (IOException)
		{
		}

		return key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
	}

	public void SetValue(string value)
	{
		using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
		key.SetValue(ValueName, value, _lastKnownKind);
	}
}

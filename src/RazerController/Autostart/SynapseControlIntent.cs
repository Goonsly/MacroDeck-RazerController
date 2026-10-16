namespace KeystoneDigital.RazerController.Autostart;

/// <summary>
/// Whether the user wants Synapse control on: true, false, or null when never decided. The plugin
/// changes Synapse's startup entry only while this is true.
/// </summary>
internal interface ISynapseControlIntent
{
	bool? Get();

	void Set(bool enabled);
}

/// <summary>Kept in the plugin's data directory, the one location that survives updates.</summary>
internal sealed class FileSynapseControlIntent(string directory) : ISynapseControlIntent
{
	private const string FileName = "synapse-control.txt";

	private readonly Lock _gate = new();

	public static FileSynapseControlIntent FromEnvironment()
	{
		var directory = Environment.GetEnvironmentVariable("MACRO_DECK_PLUGIN_DATA_DIRECTORY");
		if (string.IsNullOrWhiteSpace(directory))
		{
			directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RazerController");
		}

		return new FileSynapseControlIntent(directory);
	}

	public bool? Get()
	{
		lock (_gate)
		{
			var path = Path.Combine(directory, FileName);
			if (!File.Exists(path)) return null;

			return File.ReadAllText(path).Trim() switch
			{
				"on" => true,
				"off" => false,
				_ => null,
			};
		}
	}

	public void Set(bool enabled)
	{
		lock (_gate)
		{
			Directory.CreateDirectory(directory);
			File.WriteAllText(Path.Combine(directory, FileName), enabled ? "on" : "off");
		}
	}
}

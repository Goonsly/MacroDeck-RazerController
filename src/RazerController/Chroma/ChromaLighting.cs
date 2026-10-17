using System.Globalization;

namespace KeystoneDigital.RazerController.Chroma;

internal enum ChromaMode
{
	Off,
	Static,
	Breathing,
	Spectrum,
	Wave,
}

/// <summary>A Chroma SDK device type, as the REST API names it, and the LED grid its custom effect takes.</summary>
internal sealed record ChromaDevice(string Name, string CustomEffect, int Rows, int Columns, bool OneDimensional)
{
	public static IReadOnlyList<ChromaDevice> All { get; } =
	[
		new("keyboard", "CHROMA_CUSTOM", 6, 22, false),
		new("mouse", "CHROMA_CUSTOM2", 9, 7, false),
		new("mousepad", "CHROMA_CUSTOM", 1, 15, true),
		new("headset", "CHROMA_CUSTOM", 1, 5, true),
		new("keypad", "CHROMA_CUSTOM", 4, 5, false),
		new("chromalink", "CHROMA_CUSTOM", 1, 5, true),
	];

	public static ChromaDevice? Find(string name) => All.FirstOrDefault(device => device.Name == name);
}

/// <summary>
/// What a Chroma button asks for. The SDK itself only knows Off, Static and per-LED frames, so Breathing,
/// Spectrum and Wave are drawn here, frame by frame, and brightness scales every colour.
/// </summary>
internal sealed record ChromaLighting(ChromaMode Mode, string Color = "#00FF00", string? Color2 = null, int Brightness = 100, int Speed = 5, bool Reverse = false)
{
	public static ChromaLighting Off { get; } = new(ChromaMode.Off);

	public bool Animated => Mode is ChromaMode.Breathing or ChromaMode.Spectrum or ChromaMode.Wave;

	/// <summary>The request body for <paramref name="device"/> at <paramref name="elapsed"/> since the button was pressed.</summary>
	public object Frame(ChromaDevice device, TimeSpan elapsed)
	{
		var seconds = elapsed.TotalSeconds;

		switch (Mode)
		{
			case ChromaMode.Off:
				return new { effect = "CHROMA_NONE" };
			case ChromaMode.Wave:
				return WaveFrame(device, seconds);
			default:
				return new { effect = "CHROMA_STATIC", param = new { color = ToBgr(Dim(UniformColor(seconds))) } };
		}
	}

	/// <summary>The SDK wants colours as a BGR integer.</summary>
	public static int ToBgr(string hex) => ToBgr(ParseRgb(hex));

	public static (int R, int G, int B) ParseRgb(string hex)
	{
		var value = hex.TrimStart('#');
		if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
		{
			throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
		}

		return ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
	}

	public static int ToBgr((int R, int G, int B) color) => (color.B << 16) | (color.G << 8) | color.R;

	/// <summary>Speed 1 (slow) to 10 (fast) as one cycle's length, between <paramref name="slowest"/> and <paramref name="fastest"/> seconds.</summary>
	internal double Period(double slowest, double fastest) =>
		slowest - (Math.Clamp(Speed, 1, 10) - 1) * (slowest - fastest) / 9;

	private (int R, int G, int B) UniformColor(double seconds)
	{
		switch (Mode)
		{
			case ChromaMode.Breathing:
			{
				var period = Period(6, 1);
				var cycle = (long)Math.Floor(seconds / period);
				var phase = seconds / period - cycle;
				var level = (1 - Math.Cos(2 * Math.PI * phase)) / 2;
				var color = Color2 is not null && cycle % 2 == 1 ? ParseRgb(Color2) : ParseRgb(Color);
				return Scale(color, level);
			}
			case ChromaMode.Spectrum:
				return Hue(seconds / Period(20, 2) * 360);
			default:
				return ParseRgb(Color);
		}
	}

	private object WaveFrame(ChromaDevice device, double seconds)
	{
		var shift = seconds / Period(6, 1) * 360;
		int At(int column)
		{
			var position = (double)column / device.Columns * 360;
			return ToBgr(Dim(Hue(Reverse ? position + shift : position - shift)));
		}

		if (device.OneDimensional)
		{
			return new { effect = device.CustomEffect, param = Enumerable.Range(0, device.Columns).Select(At).ToArray() };
		}

		var row = Enumerable.Range(0, device.Columns).Select(At).ToArray();
		return new { effect = device.CustomEffect, param = Enumerable.Range(0, device.Rows).Select(_ => row).ToArray() };
	}

	private (int R, int G, int B) Dim((int R, int G, int B) color) => Scale(color, Math.Clamp(Brightness, 0, 100) / 100.0);

	private static (int R, int G, int B) Scale((int R, int G, int B) color, double factor) =>
		((int)Math.Round(color.R * factor), (int)Math.Round(color.G * factor), (int)Math.Round(color.B * factor));

	/// <summary>A fully saturated colour on the colour wheel.</summary>
	private static (int R, int G, int B) Hue(double degrees)
	{
		var h = ((degrees % 360) + 360) % 360 / 60;
		var x = 1 - Math.Abs(h % 2 - 1);
		var (r, g, b) = (int)h switch
		{
			0 => (1.0, x, 0.0),
			1 => (x, 1.0, 0.0),
			2 => (0.0, 1.0, x),
			3 => (0.0, x, 1.0),
			4 => (x, 0.0, 1.0),
			_ => (1.0, 0.0, x),
		};
		return ((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
	}
}

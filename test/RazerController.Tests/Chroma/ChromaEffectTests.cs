using System.Text.Json;
using KeystoneDigital.RazerController.Actions;
using KeystoneDigital.RazerController.Chroma;
using KeystoneDigital.RazerController.Tests.Synapse;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Chroma;

[TestFixture]
public sealed class ChromaEffectTests
{
	private sealed class ManualClock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static readonly ChromaDevice Keyboard = ChromaDevice.Find("keyboard")!;
	private static readonly ChromaDevice Mouse = ChromaDevice.Find("mouse")!;
	private static readonly ChromaDevice Headset = ChromaDevice.Find("headset")!;

	private static JsonElement Json(object body) => JsonSerializer.SerializeToElement(body);

	private static int StaticColor(ChromaLighting lighting, double seconds) =>
		Json(lighting.Frame(Keyboard, TimeSpan.FromSeconds(seconds))).GetProperty("param").GetProperty("color").GetInt32();

	[Test]
	public void Brightness_scales_the_colour()
	{
		var half = new ChromaLighting(ChromaMode.Static, "#FF8000", Brightness: 50);

		Assert.That(StaticColor(half, 0), Is.EqualTo(ChromaLighting.ToBgr((128, 64, 0))));
	}

	[Test]
	public void Breathing_starts_dark_and_peaks_halfway()
	{
		var breathing = new ChromaLighting(ChromaMode.Breathing, "#FF0000", Speed: 10);
		var period = breathing.Period(6, 1);

		Assert.That(StaticColor(breathing, 0), Is.Zero);
		Assert.That(StaticColor(breathing, period / 2), Is.EqualTo(ChromaLighting.ToBgr("#FF0000")));
	}

	[Test]
	public void Two_colour_breathing_alternates_each_breath()
	{
		var breathing = new ChromaLighting(ChromaMode.Breathing, "#FF0000", "#0000FF", Speed: 10);
		var period = breathing.Period(6, 1);

		Assert.That(StaticColor(breathing, period / 2), Is.EqualTo(ChromaLighting.ToBgr("#FF0000")));
		Assert.That(StaticColor(breathing, period * 1.5), Is.EqualTo(ChromaLighting.ToBgr("#0000FF")));
	}

	[Test]
	public void Speed_shortens_the_cycle()
	{
		Assert.That(new ChromaLighting(ChromaMode.Spectrum, Speed: 1).Period(20, 2), Is.EqualTo(20));
		Assert.That(new ChromaLighting(ChromaMode.Spectrum, Speed: 10).Period(20, 2), Is.EqualTo(2));
	}

	[Test]
	public void Spectrum_moves_round_the_colour_wheel()
	{
		var spectrum = new ChromaLighting(ChromaMode.Spectrum, Speed: 10);

		Assert.That(StaticColor(spectrum, 0), Is.EqualTo(ChromaLighting.ToBgr("#FF0000")));
		Assert.That(StaticColor(spectrum, 2.0 / 3), Is.EqualTo(ChromaLighting.ToBgr("#00FF00")));
	}

	[Test]
	public void Wave_fills_each_devices_grid()
	{
		var wave = new ChromaLighting(ChromaMode.Wave);

		var keyboard = Json(wave.Frame(Keyboard, TimeSpan.Zero));
		var mouse = Json(wave.Frame(Mouse, TimeSpan.Zero));
		var headset = Json(wave.Frame(Headset, TimeSpan.Zero));

		Assert.That(keyboard.GetProperty("effect").GetString(), Is.EqualTo("CHROMA_CUSTOM"));
		Assert.That(keyboard.GetProperty("param").GetArrayLength(), Is.EqualTo(6));
		Assert.That(keyboard.GetProperty("param")[0].GetArrayLength(), Is.EqualTo(22));
		Assert.That(mouse.GetProperty("effect").GetString(), Is.EqualTo("CHROMA_CUSTOM2"));
		Assert.That(mouse.GetProperty("param").GetArrayLength(), Is.EqualTo(9));
		Assert.That(mouse.GetProperty("param")[0].GetArrayLength(), Is.EqualTo(7));
		Assert.That(headset.GetProperty("param").GetArrayLength(), Is.EqualTo(5));
	}

	[Test]
	public void Wave_direction_reverses()
	{
		int Second(bool reverse) => Json(new ChromaLighting(ChromaMode.Wave, Reverse: reverse).Frame(Headset, TimeSpan.FromSeconds(0.5)))
			.GetProperty("param")[0].GetInt32();

		Assert.That(Second(true), Is.Not.EqualTo(Second(false)));
	}

	[Test]
	public async Task Animation_sends_new_frames_and_skips_refused_devices()
	{
		var server = new FakeChromaServer();
		server.Refused.Add("headset");
		var clock = new ManualClock();
		using var chroma = new ChromaClient(server, SynapseServiceTests.Log, [new Uri(FakeChromaServer.Endpoint)], TimeSpan.Zero, clock);

		await chroma.ApplyAsync([Keyboard, Headset], new ChromaLighting(ChromaMode.Spectrum, Speed: 10), default);
		var first = server.Effects["keyboard"].GetRawText();
		server.Requests.Clear();

		clock.Now += TimeSpan.FromSeconds(0.5);
		await chroma.AnimateAsync(default);

		Assert.That(chroma.IsAnimating, Is.True);
		Assert.That(server.Effects["keyboard"].GetRawText(), Is.Not.EqualTo(first));
		Assert.That(server.Requests, Has.None.Contains("headset"));
	}

	[Test]
	public async Task A_static_colour_is_not_animated()
	{
		var server = new FakeChromaServer();
		using var chroma = ChromaTests.Client(server);

		await chroma.ApplyAsync([Keyboard], new ChromaLighting(ChromaMode.Static, "#FF0000"), default);

		Assert.That(chroma.IsAnimating, Is.False);
	}

	[Test]
	public void Action_reads_the_effect_settings()
	{
		var lighting = ChromaLightingAction.Read(
			new Dictionary<string, object> { ["color"] = "#112233", ["color2"] = "#445566", ["brightness"] = 40, ["speed"] = 8, ["reverse"] = true },
			"breathing");

		Assert.That(lighting, Is.EqualTo(new ChromaLighting(ChromaMode.Breathing, "#112233", "#445566", 40, 8, true)));
	}

	[Test]
	public void Action_refuses_a_bad_colour() =>
		Assert.That(ChromaLightingAction.Read(new Dictionary<string, object> { ["color"] = "red" }, "static"), Is.Null);
}

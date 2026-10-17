using System.Net;
using System.Text;
using System.Text.Json;
using KeystoneDigital.RazerController.Chroma;
using KeystoneDigital.RazerController.Tests.Actions;
using KeystoneDigital.RazerController.Tests.Hid;
using KeystoneDigital.RazerController.Tests.Synapse;
using MacroDeck.Sdk.Actions;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Chroma;

/// <summary>A stand-in for the Chroma SDK REST server.</summary>
internal sealed class FakeChromaServer : HttpMessageHandler
{
	public const string Endpoint = "http://localhost:54235/razer/chromasdk";

	private int _sessions;

	public bool Running { get; set; } = true;

	/// <summary>Device types the server answers with a non-zero result, like a type this PC does not have.</summary>
	public HashSet<string> Refused { get; } = [];

	public List<string> Requests { get; } = [];

	public Dictionary<string, JsonElement> Effects { get; } = [];

	public string? Session { get; private set; }

	public void DropSession() => Session = null;

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var url = request.RequestUri!.ToString();
		Requests.Add($"{request.Method} {url}");
		if (!Running) throw new HttpRequestException("connection refused");

		if (request.Method == HttpMethod.Post && url == Endpoint)
		{
			Session = $"http://localhost:{55000 + ++_sessions}/chromasdk";
			return Json(new { sessionid = _sessions, uri = Session });
		}

		if (Session is null || !url.StartsWith(Session, StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.NotFound);

		if (request.Method == HttpMethod.Delete)
		{
			Session = null;
			return Json(new { result = 0 });
		}

		var device = url[(Session.Length + 1)..];
		if (device == "heartbeat") return Json(new { tick = 1 });
		if (Refused.Contains(device)) return Json(new { result = 1167 });

		Effects[device] = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
		return Json(new { result = 0 });
	}

	private static HttpResponseMessage Json(object body) =>
		new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}

[TestFixture]
public sealed class ChromaTests
{
	private static readonly ChromaDevice Kb = ChromaDevice.Find("keyboard")!;
	private static readonly ChromaDevice Mouse = ChromaDevice.Find("mouse")!;

	internal static ChromaClient Client(FakeChromaServer server) =>
		new(server, SynapseServiceTests.Log, [new Uri(FakeChromaServer.Endpoint)], TimeSpan.Zero);

	private static Dictionary<string, object?> P(params (string Key, object? Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

	[TestCase("#FF0000", 0x0000FF)]
	[TestCase("#00FF00", 0x00FF00)]
	[TestCase("#0000FF", 0xFF0000)]
	[TestCase("#123456", 0x563412)]
	public void Colours_are_sent_as_BGR(string hex, int bgr) => Assert.That(ChromaLighting.ToBgr(hex), Is.EqualTo(bgr));

	[Test]
	public async Task Static_opens_one_session_and_sets_the_colour()
	{
		var server = new FakeChromaServer();
		using var chroma = Client(server);

		Assert.That(await chroma.ApplyAsync([Kb], new ChromaLighting(ChromaMode.Static, "#FF0000"), default), Is.EqualTo(ChromaOutcome.Applied));
		Assert.That(await chroma.ApplyAsync([Kb], ChromaLighting.Off, default), Is.EqualTo(ChromaOutcome.Applied));

		Assert.That(server.Requests.Count(r => r.StartsWith("POST", StringComparison.Ordinal)), Is.EqualTo(1));
		Assert.That(server.Effects["keyboard"].GetProperty("effect").GetString(), Is.EqualTo("CHROMA_NONE"));
	}

	[Test]
	public async Task Static_sends_the_colour_param()
	{
		var server = new FakeChromaServer();
		using var chroma = Client(server);

		await chroma.ApplyAsync([Mouse], new ChromaLighting(ChromaMode.Static, "#FF0000"), default);

		Assert.That(server.Effects["mouse"].GetProperty("effect").GetString(), Is.EqualTo("CHROMA_STATIC"));
		Assert.That(server.Effects["mouse"].GetProperty("param").GetProperty("color").GetInt32(), Is.EqualTo(0x0000FF));
	}

	[Test]
	public async Task No_server_is_Unavailable()
	{
		var server = new FakeChromaServer { Running = false };
		using var chroma = Client(server);

		Assert.That(await chroma.ApplyAsync([Kb], ChromaLighting.Off, default), Is.EqualTo(ChromaOutcome.Unavailable));
	}

	[Test]
	public async Task A_lost_session_is_reopened_on_the_next_press()
	{
		var server = new FakeChromaServer();
		using var chroma = Client(server);
		await chroma.ApplyAsync([Kb], ChromaLighting.Off, default);

		server.DropSession();

		Assert.That(await chroma.ApplyAsync([Kb], new ChromaLighting(ChromaMode.Static, "#0000FF"), default), Is.EqualTo(ChromaOutcome.Applied));
		Assert.That(server.Requests.Count(r => r.StartsWith("POST", StringComparison.Ordinal)), Is.EqualTo(2));
	}

	[Test]
	public async Task The_heartbeat_puts_effects_back_after_the_session_is_lost()
	{
		var server = new FakeChromaServer();
		using var chroma = Client(server);
		await chroma.ApplyAsync([Kb, Mouse], new ChromaLighting(ChromaMode.Static, "#FF0000"), default);

		server.DropSession();
		server.Effects.Clear();
		await chroma.HeartbeatAsync(default);

		Assert.That(server.Effects.Keys, Is.EquivalentTo(new[] { "keyboard", "mouse" }));
	}

	[Test]
	public async Task The_heartbeat_opens_nothing_when_no_effect_is_held()
	{
		var server = new FakeChromaServer();
		using var chroma = Client(server);

		await chroma.HeartbeatAsync(default);

		Assert.That(server.Requests, Is.Empty);
	}

	[Test]
	public async Task Release_ends_the_session_and_forgets_the_effects()
	{
		var server = new FakeChromaServer();
		using var chroma = Client(server);
		await chroma.ApplyAsync([Kb], ChromaLighting.Off, default);

		await chroma.ReleaseAsync(default);
		await chroma.HeartbeatAsync(default);

		Assert.That(server.Session, Is.Null);
		Assert.That(chroma.HasSession, Is.False);
		Assert.That(server.Requests.Last(), Does.StartWith("DELETE"));
	}

	[Test]
	public async Task All_devices_succeeds_when_some_types_are_missing_and_fails_when_none_take_it()
	{
		var server = new FakeChromaServer();
		server.Refused.UnionWith(["headset", "keypad", "chromalink", "mousepad"]);
		using var chroma = Client(server);

		Assert.That(await chroma.ApplyAsync(ChromaDevice.All, ChromaLighting.Off, default), Is.EqualTo(ChromaOutcome.Applied));

		server.Refused.UnionWith(["keyboard", "mouse"]);
		Assert.That(await chroma.ApplyAsync(ChromaDevice.All, ChromaLighting.Off, default), Is.EqualTo(ChromaOutcome.Rejected));
	}

	[Test]
	public async Task The_action_works_with_Synapse_control_off()
	{
		var server = new FakeChromaServer();
		var synapse = new KeystoneDigital.RazerController.Synapse.SynapseService(FakeInspector.FromFixture(), SynapseServiceTests.Log, SynapseServiceTests.Fast, controlEnabled: () => false);
		await synapse.RefreshAsync(default);
		await using var harness = ActionTests.CreateHarness(synapse, MouseDpiServiceTests.ServiceFor(null), chroma: Client(server), intent: new Autostart.FakeIntent(null));
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync("chroma-lighting", P(("devices", "keyboard"), ("effect", "static"), ("color", "#00FF00")));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		Assert.That(server.Effects["keyboard"].GetProperty("param").GetProperty("color").GetInt32(), Is.EqualTo(0x00FF00));
	}

	[Test]
	public async Task The_action_reports_a_missing_Chroma_module()
	{
		await using var harness = ActionTests.CreateHarness(
			SynapseServiceTests.CreateService(FakeInspector.FromFixture()), MouseDpiServiceTests.ServiceFor(null), chroma: Client(new FakeChromaServer { Running = false }));
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync("chroma-lighting", P(("devices", "all"), ("effect", "off")));

		Assert.That(ActionTests.ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task Synapse_lighting_releases_the_session()
	{
		var server = new FakeChromaServer();
		await using var harness = ActionTests.CreateHarness(SynapseServiceTests.CreateService(FakeInspector.FromFixture()), MouseDpiServiceTests.ServiceFor(null), chroma: Client(server));
		await harness.InitializeIntegrationsAsync();
		await harness.Actions.ExecuteAsync("chroma-lighting", P(("devices", "mouse"), ("effect", "off")));

		var outcome = await harness.Actions.ExecuteAsync("chroma-lighting", P(("effect", "synapse")));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		Assert.That(server.Session, Is.Null);
	}
}

using System.Text.Json;
using KeystoneDigital.RazerController.Synapse;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Synapse;

internal static class Fixture
{
	public static JsonElement Load(string name)
	{
		using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name)));
		return doc.RootElement.Clone();
	}

	public static JsonElement Parse(string json)
	{
		using var doc = JsonDocument.Parse(json);
		return doc.RootElement.Clone();
	}
}

[TestFixture]
public sealed class CatalogParserTests
{
	[Test]
	public void Parses_both_live_devices()
	{
		var result = SynapseCatalogParser.Parse(Fixture.Load("catalog-snapshot.json"));

		var mouse = result.Devices.Single(d => d.ProductId == 103);
		var keyboard = result.Devices.Single(d => d.ProductId == 554);

		Assert.That(result.SynapseVersion, Is.EqualTo("4.0.827"));
		Assert.That(mouse.Category, Is.EqualTo(DeviceCategory.Mouse));
		Assert.That(mouse.Name, Is.EqualTo("Razer Naga Trinity"));
		Assert.That(mouse.Serial, Does.StartWith("SERIAL-"));
		Assert.That(mouse.Channel, Does.StartWith("usb_5426_103_{").And.EndWith("}_mw"));
		Assert.That(mouse.Profiles, Has.Count.EqualTo(19));
		Assert.That(mouse.ActiveProfileName, Is.Not.Null);
		Assert.That(keyboard.Category, Is.EqualTo(DeviceCategory.Keyboard));
		Assert.That(keyboard.Name, Is.EqualTo("Razer Cynosa Chroma"));
		Assert.That(keyboard.ActiveProfileName, Is.EqualTo("Desktop | Default"));
		Assert.That(keyboard.Effect, Is.EqualTo(new LightingEffect(1, "#00ff00")));
		Assert.That(keyboard.GamingMode, Is.Not.Null);
		Assert.That(keyboard.GamingMode!.On, Is.False);
		Assert.That(keyboard.GamingMode.Raw.GetProperty("scancode").GetInt32(), Is.EqualTo(29));
	}

	[Test]
	public void Null_snapshot_yields_no_devices() => Assert.That(SynapseCatalogParser.Parse(null).Devices, Is.Empty);

	[Test]
	public void A_window_without_a_storage_document_is_skipped()
	{
		var json = """{"version":"4.0.827","windows":[{"productId":999,"title":"usb_5426_999_{X}_mw"}],"storage":{"connectedDeviceInfo":"[]","devices":{"999":null}}}""";

		Assert.That(SynapseCatalogParser.Parse(Fixture.Parse(json)).Devices, Is.Empty);
	}

	[Test]
	public void Malformed_storage_json_does_not_throw()
	{
		var json = """{"version":"x","windows":[{"productId":103,"title":"t"}],"storage":{"connectedDeviceInfo":"{oops","devices":{"103":"[1,2"}}}""";

		Assert.That(() => SynapseCatalogParser.Parse(Fixture.Parse(json)), Throws.Nothing);
	}

	[Test]
	public void Device_metadata_is_the_fallback_when_connected_device_info_has_no_entry()
	{
		var doc = JsonSerializer.Serialize(new
		{
			category = "KEYBOARD",
			activeProfile = "g1",
			profiles = new[] { new { guid = "g1", name = "One" } },
			deviceMetadatas = new Dictionary<string, object> { ["SER9"] = new { name = "Some Keyboard" } },
		});
		var json = JsonSerializer.Serialize(new
		{
			version = "4",
			windows = new[] { new { productId = 600, title = "usb_5426_600_{C}_mw" } },
			storage = new { connectedDeviceInfo = "[]", devices = new Dictionary<string, string> { ["600"] = doc } },
		});

		var device = SynapseCatalogParser.Parse(Fixture.Parse(json)).Devices.Single();

		Assert.That(device.Serial, Is.EqualTo("SER9"));
		Assert.That(device.Name, Is.EqualTo("Some Keyboard"));
		Assert.That(device.Category, Is.EqualTo(DeviceCategory.Keyboard));
		Assert.That(device.Effect, Is.Null);
		Assert.That(device.GamingMode, Is.Null);
	}

	[Test]
	public void A_device_without_any_serial_is_skipped()
	{
		var doc = """{"category":"MOUSE","activeProfile":"g","profiles":[{"guid":"g","name":"n"}]}""";
		var json = JsonSerializer.Serialize(new
		{
			version = "4",
			windows = new[] { new { productId = 600, title = "t" } },
			storage = new { connectedDeviceInfo = "[]", devices = new Dictionary<string, string> { ["600"] = doc } },
		});

		Assert.That(SynapseCatalogParser.Parse(Fixture.Parse(json)).Devices, Is.Empty);
	}
}

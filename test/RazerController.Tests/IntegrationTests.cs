using KeystoneDigital.RazerController.Tests.Actions;
using KeystoneDigital.RazerController.Tests.Hid;
using KeystoneDigital.RazerController.Tests.Synapse;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests;

[TestFixture]
public sealed class IntegrationTests
{
	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = ActionTests.CreateHarness(
			SynapseServiceTests.CreateService(FakeInspector.FromFixture()),
			MouseDpiServiceTests.ServiceFor(new SimulatedMouse(800)));

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.keystonedigital.razer"));
	}

	[Test]
	public void Every_resx_key_resolves()
	{
		foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
		{
			Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text), Is.True, key);
			Assert.That(text, Is.Not.Empty, key);
		}
	}
}

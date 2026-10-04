using KeystoneDigital.RazerController;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;

var builder = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog);

builder.Services.AddRazerServices();

var plugin = builder
	.RegisterIntegration(RazerIntegration.Create)
	.Build();

await plugin.RunAsync();

using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Hid;
using KeystoneDigital.RazerController.Synapse;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KeystoneDigital.RazerController;

internal static class RazerServices
{
	/// <summary>The real machine: Synapse's inspector on 9229, the system's HID devices, the HKCU Run key.</summary>
	public static IServiceCollection AddRazerServices(this IServiceCollection services)
	{
		services.AddSingleton<ICdpTargetFetcher, HttpCdpTargetFetcher>();
		services.AddSingleton<IRawDuplexFactory, WebSocketRawDuplexFactory>();
		services.AddSingleton<IInspector>(sp => new CdpInspector(sp.GetRequiredService<ICdpTargetFetcher>(), sp.GetRequiredService<IRawDuplexFactory>()));
		services.AddSingleton(sp => new SynapseService(sp.GetRequiredService<IInspector>(), sp.GetRequiredService<ILogger>()));
		services.AddSingleton(sp => MouseDpiService.CreateDefault(sp.GetRequiredService<ILogger>()));
		services.AddSingleton<IAutostartRegistry, WindowsAutostartRegistry>();
		services.AddSingleton<ISynapseControlIntent>(_ => FileSynapseControlIntent.FromEnvironment());
		services.AddSingleton<ISynapseProcess, WindowsSynapseProcess>();
		services.AddSingleton(sp => new InspectorGuard(
			sp.GetRequiredService<IAutostartRegistry>(), sp.GetRequiredService<ISynapseControlIntent>(), sp.GetRequiredService<ISynapseProcess>(),
			sp.GetRequiredService<ILogger>(), TimeProvider.System));
		services.AddHostedService(sp => sp.GetRequiredService<SynapseService>());
		services.AddHostedService(sp => new InspectorGuardService(sp.GetRequiredService<InspectorGuard>(), sp.GetRequiredService<SynapseService>(), sp.GetRequiredService<ILogger>()));
		services.AddHostedService(sp => new MouseDpiPoller(sp.GetRequiredService<MouseDpiService>(), TimeSpan.FromSeconds(2)));
		return services;
	}

	/// <summary>Pre-built services, for tests.</summary>
	public static IServiceCollection AddRazerServices(this IServiceCollection services, SynapseService synapse, MouseDpiService dpi, IAutostartRegistry autostart)
	{
		services.AddSingleton(synapse);
		services.AddSingleton(dpi);
		services.AddSingleton(autostart);
		services.AddSingleton<ISynapseControlIntent>(new FileSynapseControlIntent(Path.Combine(Path.GetTempPath(), "razer-tests-" + Guid.NewGuid().ToString("N"))));
		return services;
	}
}

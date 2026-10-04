using System.Text.Json;

namespace KeystoneDigital.RazerController.Synapse;

/// <summary>
/// The JavaScript evaluated inside Synapse's main process. Every value from outside this assembly is
/// JSON-serialized and inserted as a literal, never concatenated as source.
/// </summary>
internal static class InspectorScripts
{
	private const string ImportSnippet = """
		const getElectron = () => {
			if (typeof process !== "undefined" && typeof process.mainModule?.require === "function") {
				try { return process.mainModule.require("electron"); } catch (_) {}
			}
			if (typeof require === "function") return require("electron");
			throw new Error("Electron import unavailable");
		};
		""";

	/// <summary>process.type, process.execPath and whether Electron is reachable: the identity check.</summary>
	public static readonly string Probe = """
		(() => {
		__IMPORT__
		const result = {
			processType: typeof process !== "undefined" ? process.type : null,
			execPath: typeof process !== "undefined" ? process.execPath : null,
			electronAvailable: false
		};
		try {
			const electron = getElectron();
			result.electronAvailable = typeof electron.webContents?.getAllWebContents === "function";
		} catch (_) {}
		return result;
		})()
		""".Replace("__IMPORT__", ImportSnippet, StringComparison.Ordinal);

	/// <summary>
	/// Lists every device middleware window and reads Synapse's storage from one other apps.razer.com
	/// renderer. Storage values come back as raw strings so all parsing happens in C#.
	/// </summary>
	public static readonly string Catalog = """
		(async () => {
		__IMPORT__
		const electron = getElectron();
		const all = electron.webContents.getAllWebContents().filter(wc => wc && !wc.isDestroyed());
		const windows = [];
		let reader = null;
		for (const wc of all) {
			let url;
			try { url = new URL(wc.getURL()); } catch (_) { continue; }
			if (url.origin !== "https://apps.razer.com") continue;
			const m = url.pathname.match(/^\/synapse\/products\/(\d+)\/mw\//);
			if (m) windows.push({ productId: Number(m[1]), title: wc.getTitle() });
			else if (!reader) reader = wc;
		}
		if (!reader) return { version: electron.app.getVersion(), windows, storage: null };
		const pids = windows.map(w => w.productId);
		const storage = await reader.executeJavaScript("(() => { const pids = " + JSON.stringify(pids) + "; const out = { connectedDeviceInfo: localStorage.getItem('connectedDeviceInfo'), devices: {} }; for (const pid of pids) out.devices[pid] = localStorage.getItem('synapse_' + pid); return out; })()", true);
		return { version: electron.app.getVersion(), windows, storage };
		})()
		""".Replace("__IMPORT__", ImportSnippet, StringComparison.Ordinal);

	private const string CommandTemplate = """
		(async () => {
		__IMPORT__
		const target = __PAYLOAD__;
		const electron = getElectron();
		const sender = electron.webContents.getAllWebContents().find(wc => {
			if (!wc || wc.isDestroyed()) return false;
			try { const u = new URL(wc.getURL()); return u.origin === "https://apps.razer.com" && !u.pathname.includes("/mw/"); } catch (_) { return false; }
		});
		if (!sender) return { sent: false };
		const script = "(() => { const t = " + JSON.stringify(target) + "; const bc = new BroadcastChannel(t.channel); bc.postMessage({ msgName: 'userCommand', msgData: { type: t.type, payload: t.payload } }); bc.close(); return true; })()";
		const sent = await sender.executeJavaScript(script, true);
		return { sent: sent === true };
		})()
		""";

	/// <summary>Posts Synapse's own userCommand envelope on a device's BroadcastChannel.</summary>
	public static string Command(string channel, string eventType, object payload)
	{
		var target = JsonSerializer.Serialize(new { channel, type = eventType, payload });

		return CommandTemplate
			.Replace("__IMPORT__", ImportSnippet, StringComparison.Ordinal)
			.Replace("__PAYLOAD__", target, StringComparison.Ordinal);
	}
}

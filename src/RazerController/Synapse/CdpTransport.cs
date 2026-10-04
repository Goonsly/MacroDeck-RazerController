using System.Net.WebSockets;
using System.Text;

namespace KeystoneDigital.RazerController.Synapse;

internal interface ICdpTargetFetcher
{
	Task<string> FetchAsync(int port, CancellationToken cancellationToken);
}

internal interface IRawDuplex : IDisposable
{
	Task SendAsync(string message, CancellationToken cancellationToken);

	Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}

internal interface IRawDuplexFactory
{
	Task<IRawDuplex> ConnectAsync(Uri webSocketDebuggerUrl, CancellationToken cancellationToken);
}

internal sealed class HttpCdpTargetFetcher : ICdpTargetFetcher, IDisposable
{
	// No proxy: a system proxy or PAC script would otherwise see a loopback debug session.
	private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
	{
		Timeout = TimeSpan.FromSeconds(5),
	};

	public async Task<string> FetchAsync(int port, CancellationToken cancellationToken)
	{
		try
		{
			using var response = await _client.GetAsync(new Uri($"http://127.0.0.1:{port}/json/list"), cancellationToken).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();
			return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
		{
			throw new SynapseInspectorException("inspector_unreachable", "Synapse's local inspector is unavailable.", ex);
		}
	}

	public void Dispose() => _client.Dispose();
}

internal sealed class WebSocketRawDuplexFactory : IRawDuplexFactory
{
	public async Task<IRawDuplex> ConnectAsync(Uri webSocketDebuggerUrl, CancellationToken cancellationToken)
	{
		var socket = new ClientWebSocket();

		// Same reason as the HTTP half: this handshake carries a code-execution session.
		socket.Options.Proxy = null;

		try
		{
			await socket.ConnectAsync(webSocketDebuggerUrl, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
		{
			socket.Dispose();
			throw new SynapseInspectorException("transport_lost", "Could not connect to Synapse's local inspector.", ex);
		}

		return new WebSocketRawDuplex(socket);
	}
}

internal sealed class WebSocketRawDuplex : IRawDuplex
{
	private readonly ClientWebSocket _socket;

	public WebSocketRawDuplex(ClientWebSocket socket) => _socket = socket;

	public async Task SendAsync(string message, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(message);
		await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
	}

	public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();

		while (true)
		{
			var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
			if (result.MessageType == WebSocketMessageType.Close) return null;

			stream.Write(buffer, 0, result.Count);
			if (result.EndOfMessage) break;
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	public void Dispose()
	{
		try
		{
			_socket.Abort();
		}
		catch (ObjectDisposedException)
		{
		}

		_socket.Dispose();
	}
}

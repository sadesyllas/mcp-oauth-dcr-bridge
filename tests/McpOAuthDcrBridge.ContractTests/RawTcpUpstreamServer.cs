using System.Net;
using System.Net.Sockets;
using System.Text;

namespace McpOAuthDcrBridge.ContractTests;

/// <summary>
/// A minimal raw-socket fake upstream that accepts a connection, reads and discards the request head,
/// then writes a configurable byte payload that need not be valid HTTP at all. Used to prove the
/// bridge maps a protocol-invalid upstream reply to a bounded gateway error rather than hanging or
/// relaying the raw bytes downstream.
/// </summary>
internal sealed class RawTcpUpstreamServer : IAsyncDisposable
{
    private const string DefaultInvalidResponse = "NOT-HTTP/9.9 garbage\r\n\r\n";

    private readonly TcpListener listener;
    private readonly Task acceptLoop;
    private readonly CancellationTokenSource stopping = new();
    private int connectionCount;

    private RawTcpUpstreamServer(TcpListener listener, string responsePayload)
    {
        this.listener = listener;
        acceptLoop = AcceptLoopAsync(responsePayload, stopping.Token);
    }

    /// <summary>Gets the number of connections this server has accepted. Safe to read under concurrent connections.</summary>
    public int ConnectionCount => connectionCount;

    /// <summary>Gets the absolute base URL (scheme, host, and port) for this server.</summary>
    public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    /// <summary>Starts a raw TCP server on an ephemeral local port that replies with an invalid HTTP payload.</summary>
    /// <param name="responsePayload">The raw bytes (as text) to write on every accepted connection.</param>
    public static Task<RawTcpUpstreamServer> StartAsync(string responsePayload = DefaultInvalidResponse)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new RawTcpUpstreamServer(listener, responsePayload));
    }

    private async Task AcceptLoopAsync(string responsePayload, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var socket = await listener.AcceptSocketAsync(cancellationToken);
                Interlocked.Increment(ref connectionCount);
                using var stream = new NetworkStream(socket, ownsSocket: false);
                var buffer = new byte[4096];
                try
                {
                    await stream.ReadAsync(buffer, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (Exception)
                {
                    // The request head may not arrive (or may exceed this best-effort read) before the
                    // connection closes; either way, the server still replies with the invalid payload.
                }

                var responseBytes = Encoding.ASCII.GetBytes(responsePayload);
                await stream.WriteAsync(responseBytes, cancellationToken);
                socket.Shutdown(SocketShutdown.Send);
            }
        }
        catch (Exception)
        {
            // Expected once StopAsync cancels the accept loop or the listener is disposed.
        }
    }

    /// <summary>Stops accepting connections and releases the listener.</summary>
    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        try
        {
            await acceptLoop;
        }
        catch (Exception)
        {
            // Already logged/expected via the accept loop's own handling.
        }

        stopping.Dispose();
    }
}

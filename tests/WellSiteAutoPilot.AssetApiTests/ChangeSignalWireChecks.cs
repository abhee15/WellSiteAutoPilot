using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

/// <summary>
/// ADR-008 over the real host: the hub authenticates like the REST API, admits only users who
/// hold an area's read permission, and sends nothing but the area name after a commit. This
/// speaks the SignalR JSON protocol directly over a WebSocket with the CI test header. It does
/// NOT prove Windows Negotiate behaviour for browser WebSockets (issue #92).
/// </summary>
internal static class ChangeSignalWireChecks
{
    private const char RecordSeparator = '\u001e';
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    public static async Task RunAsync(Uri origin, HttpClient anonymous, HttpClient admin, string adminIdentity,
        string unprivilegedIdentity)
    {
        using (var rejected = await anonymous.PostAsync("/hubs/changes/negotiate?negotiateVersion=1", null))
            if (rejected.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException(
                    "Hub negotiate without identity must challenge, got " + (int)rejected.StatusCode + ".");

        using var listener = await ConnectAsync(origin, adminIdentity);
        var read = ReadUntilAsync(listener, frame => frame.Contains("\"target\":\"changed\"", StringComparison.Ordinal));
        // The handshake reply is sent before the hub admits the connection to its groups, so a
        // change committed too early is legitimately missed (polling recovers it in the UI).
        // Keep committing until the first signal arrives.
        var keys = new List<string>();
        for (var attempt = 0; attempt < 24 && !read.IsCompleted; attempt++)
        {
            var key = "ci-signal-" + Guid.NewGuid().ToString("N");
            keys.Add(key);
            using (var created = await admin.PostAsJsonAsync("/api/v1/asset-types",
                       new { key, displayName = "CI Signal", attributeSchemaJson = """{"type":"object"}""" }))
                if (created.StatusCode != HttpStatusCode.Created)
                    throw new InvalidOperationException("Signal trigger asset type create failed: " + (int)created.StatusCode);
            await Task.WhenAny(read, Task.Delay(500));
        }

        var message = await read;
        using var document = JsonDocument.Parse(message);
        var arguments = document.RootElement.GetProperty("arguments");
        if (document.RootElement.GetProperty("type").GetInt32() != 1 || arguments.GetArrayLength() != 1 ||
            arguments[0].GetString() != "assets" || keys.Any(key => message.Contains(key, StringComparison.Ordinal)))
            throw new InvalidOperationException("The change signal must carry exactly the area name: " + message);

        // A user without any read permission is closed, not left connected with nothing to hear.
        using var denied = await ConnectAsync(origin, unprivilegedIdentity);
        var buffer = new byte[256];
        using var timeout = new CancellationTokenSource(WaitLimit);
        try
        {
            while (true)
            {
                var result = await denied.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (WebSocketException)
        {
            // An abortive close is equally a refusal.
        }

        if (denied.State == WebSocketState.Open)
            throw new InvalidOperationException("A user without read permissions stayed connected to the hub.");
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri origin, string identity)
    {
        using var http = new HttpClient { BaseAddress = origin };
        http.DefaultRequestHeaders.Add("X-WSA-Test-Identity", identity);
        using var negotiated = await http.PostAsync("/hubs/changes/negotiate?negotiateVersion=1", null);
        if (negotiated.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException("Hub negotiate expected 200, got " + (int)negotiated.StatusCode + ".");
        using var negotiation = JsonDocument.Parse(await negotiated.Content.ReadAsStringAsync());
        var token = negotiation.RootElement.TryGetProperty("connectionToken", out var tokenElement)
            ? tokenElement.GetString()
            : negotiation.RootElement.GetProperty("connectionId").GetString();

        var builder = new UriBuilder(origin)
        {
            Scheme = origin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/hubs/changes",
            Query = "id=" + Uri.EscapeDataString(token ?? "")
        };
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("X-WSA-Test-Identity", identity);
            using var timeout = new CancellationTokenSource(WaitLimit);
            await socket.ConnectAsync(builder.Uri, timeout.Token);
            var handshake = Encoding.UTF8.GetBytes("""{"protocol":"json","version":1}""" + RecordSeparator);
            await socket.SendAsync(new ArraySegment<byte>(handshake), WebSocketMessageType.Text, true, timeout.Token);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadUntilAsync(ClientWebSocket socket, Func<string, bool> match)
    {
        using var timeout = new CancellationTokenSource(WaitLimit);
        var buffer = new byte[4096];
        var pending = new StringBuilder();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("The hub closed the connection before sending the change signal.");
            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;
            var text = pending.ToString();
            pending.Clear();
            foreach (var frame in text.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
                if (match(frame)) return frame;
        }
    }
}

using System.Net.Sockets;
using System.Text;

internal static class BrokerAuthenticationChecks
{
    public static async Task RunAsync(string url)
    {
        // Probe the wire directly so client reconnect behavior cannot hide anonymous acceptance.
        var address = new Uri(url);
        foreach (var connect in new[] { "CONNECT {\"verbose\":false}", "CONNECT {\"verbose\":false,\"auth_token\":\"incorrect-test-token\"}" })
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var client = new TcpClient();
            await client.ConnectAsync(address.Host, address.Port, deadline.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            var info = await reader.ReadLineAsync(deadline.Token);
            if (info?.StartsWith("INFO ", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Broker did not provide protocol information.");
            await writer.WriteAsync((connect + "\r\nPING\r\n").AsMemory(), deadline.Token);
            var denied = false;
            for (var line = 0; line < 5; line++)
            {
                var response = await reader.ReadLineAsync(deadline.Token);
                if (response is null) break;
                if (response.StartsWith("-ERR", StringComparison.Ordinal) && response.Contains("Authorization", StringComparison.OrdinalIgnoreCase))
                { denied = true; break; }
                if (response == "PONG") throw new InvalidOperationException("Broker accepted an unauthenticated/incorrectly authenticated local client.");
            }
            if (!denied) throw new InvalidOperationException("Broker authentication refusal was not observed.");
        }
        Console.WriteLine("Broker rejected anonymous and wrong-token clients; authenticated topology/package checks follow.");
    }
}

using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FactorySoundStudio.Core;

internal sealed class DoIpSession : IAsyncDisposable
{
    private TcpClient? tcp;
    private NetworkStream? stream;
    private readonly SemaphoreSlim sequential = new(1, 1);
    private readonly ushort tester;
    public string RemoteIp { get; }
    public bool IsActive { get; private set; }

    private DoIpSession(string ip, ushort testerAddress)
    {
        RemoteIp = ip;
        tester = testerAddress;
    }

    public static async Task<DoIpSession> OpenAsync(string ip, IPAddress local, ushort tester, CancellationToken token)
    {
        if (!IPAddress.TryParse(ip, out var remote) || remote.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Enter an IPv4 address for the vehicle.");
        if (tester < 0x0E00 || tester > 0x0FFF)
            throw new ArgumentException("Tester address must be in range 0x0E00–0x0FFF.");
        var session = new DoIpSession(ip, tester);
        try
        {
            session.tcp = new TcpClient(new IPEndPoint(local, 0));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await session.tcp.ConnectAsync(remote, 13400, timeout.Token);
            session.stream = session.tcp.GetStream();
            await session.ActivateAsync(token);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private async Task ActivateAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await SendAsync(DoIpCodec.RoutingRequest, DoIpCodec.RoutingActivation(tester), timeout.Token);
        for (int count = 0; count < 8; count++)
        {
            var frame = await ReceiveAsync(timeout.Token);
            if (frame.Type == DoIpCodec.AliveCheckRequest)
            {
                await ReplyToAliveCheck(timeout.Token);
                continue;
            }
            if (frame.Type == 0x0000) throw new InvalidOperationException("DoIP header was rejected by gateway.");
            if (frame.Type != DoIpCodec.RoutingResponse) continue;
            if (frame.Payload.Length != 9 && frame.Payload.Length != 13)
                throw new FormatException("Routing activation response malformed.");
            ushort returnedTester = DoIpCodec.ReadUInt16(frame.Payload, 0);
            if (returnedTester != tester) throw new InvalidOperationException("Routing activation source mismatch.");
            byte code = frame.Payload[4];
            if (code == 0x10) { IsActive = true; return; }
            if (code == 0x11)
                throw new InvalidOperationException("Gateway requires additional confirmation. This prototype does not bypass security.");
            string reason = code switch
            {
                0x00 => "unknown tester source address",
                0x01 => "too many diagnostic connections",
                0x03 => "tester source address already in use",
                0x04 => "authentication required",
                0x05 => "confirmation rejected",
                0x06 => "activation type unsupported",
                0x07 => "TLS-encrypted connection required",
                _ => "gateway policy"
            };
            throw new InvalidOperationException($"Routing activation denied (0x{code:X2}: {reason}).");
        }
        throw new TimeoutException("No routing activation response.");
    }

    public async Task<string> ReadDidAsync(ushort target, ushort did, CancellationToken cancellation)
    {
        if (!IsActive) throw new InvalidOperationException("Not connected to DoIP gateway.");
        if (target == tester) throw new ArgumentException("Target cannot equal the tester address.");
        // This call is intentionally the only diagnostic transmit path exposed by the project.
        var uds = SafeDid.Request(did);
        await sequential.WaitAsync(cancellation);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(9));
            await SendAsync(DoIpCodec.DiagnosticMessage, DoIpCodec.Diagnostic(tester, target, uds), timeout.Token);
            for (int i = 0; i < 48; i++)
            {
                var frame = await ReceiveAsync(timeout.Token);
                if (frame.Type == DoIpCodec.AliveCheckRequest)
                {
                    await ReplyToAliveCheck(timeout.Token);
                    continue;
                }
                if (frame.Type == DoIpCodec.DiagnosticNack)
                    throw new InvalidOperationException($"DoIP diagnostic NACK (0x{(frame.Payload.Length > 4 ? frame.Payload[4] : (byte)0):X2}).");
                if (frame.Type == DoIpCodec.DiagnosticAck) continue;
                if (frame.Type != DoIpCodec.DiagnosticMessage || frame.Payload.Length < 5) continue;
                var sender = DoIpCodec.ReadUInt16(frame.Payload, 0);
                var destination = DoIpCodec.ReadUInt16(frame.Payload, 2);
                if (sender != target || destination != tester) continue;
                byte[] reply = frame.Payload[4..];
                if (reply.Length >= 3 && reply[0] == 0x7F && reply[1] == 0x22 && reply[2] == 0x78)
                    continue; // response pending; remain inside bounded timeout
                return SafeDid.DescribeResponse(did, reply);
            }
            return "No matching ECU response";
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return "Timed out (ECU unavailable or gateway restricted)";
        }
        finally { sequential.Release(); }
    }

    private async Task SendAsync(ushort type, byte[] data, CancellationToken token)
    {
        if (stream == null) throw new InvalidOperationException("Not connected.");
        await stream.WriteAsync(DoIpCodec.Encode(type, data).AsMemory(), token);
    }

    private async Task<DoIpFrame> ReceiveAsync(CancellationToken token)
    {
        if (stream == null) throw new InvalidOperationException("Not connected.");
        byte[] header = new byte[8];
        await stream.ReadExactlyAsync(header, token);
        byte version = header[0];
        if ((version != 0x02 && version != 0x03) || header[1] != (byte)~version)
            throw new FormatException("Invalid DoIP header from remote entity.");
        uint size = ((uint)header[4] << 24) | ((uint)header[5] << 16) | ((uint)header[6] << 8) | header[7];
        if (size > DoIpCodec.MaxPacketBytes) throw new FormatException("Excessive DoIP payload length.");
        byte[] data = new byte[8 + (int)size];
        header.CopyTo(data, 0);
        if (size != 0) await stream.ReadExactlyAsync(data.AsMemory(8, (int)size), token);
        return DoIpCodec.Decode(data);
    }

    private Task ReplyToAliveCheck(CancellationToken token) =>
        SendAsync(DoIpCodec.AliveCheckResponse, new byte[] { (byte)(tester >> 8), (byte)tester }, token);

    public ValueTask DisposeAsync()
    {
        IsActive = false;
        try { stream?.Dispose(); tcp?.Dispose(); } catch { }
        sequential.Dispose();
        return ValueTask.CompletedTask;
    }
}

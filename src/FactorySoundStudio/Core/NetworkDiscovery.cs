using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FactorySoundStudio.Core;

internal sealed record LocalAdapter(string Name, IPAddress Address, IPAddress Mask, bool Ethernet)
{
    public override string ToString() => $"{Name} — {Address} {(Ethernet ? "[Ethernet]" : "[other]")}";
}

internal static class NetworkDiscovery
{
    public static List<LocalAdapter> AvailableAdapters()
    {
        var adapters = new List<LocalAdapter>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (addr.IPv4Mask == null) continue;
                adapters.Add(new LocalAdapter(nic.Name, addr.Address, addr.IPv4Mask,
                    nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet));
            }
        }
        return adapters.OrderByDescending(a => a.Ethernet)
                       .ThenBy(a => a.Name).ToList();
    }

    public static IPAddress DirectedBroadcast(IPAddress local, IPAddress mask)
    {
        byte[] ip = local.GetAddressBytes();
        byte[] subnet = mask.GetAddressBytes();
        if (ip.Length != 4 || subnet.Length != 4) throw new ArgumentException("IPv4 required.");
        return new IPAddress(Enumerable.Range(0, 4)
            .Select(i => (byte)(ip[i] | ~subnet[i])).ToArray());
    }

    public static async Task<List<VehicleNode>> DiscoverAsync(LocalAdapter adapter, TimeSpan duration, CancellationToken token)
    {
        using var client = new UdpClient(new IPEndPoint(adapter.Address, 0));
        client.EnableBroadcast = true;
        var request = DoIpCodec.Encode(DoIpCodec.DiscoveryRequest, Array.Empty<byte>());
        var destinations = new List<IPAddress> { DirectedBroadcast(adapter.Address, adapter.Mask) };
        if (!destinations.Contains(IPAddress.Broadcast)) destinations.Add(IPAddress.Broadcast);
        foreach (var dest in destinations)
        {
            try { await client.SendAsync(request.AsMemory(), new IPEndPoint(dest, 13400), token); }
            catch (SocketException) { /* fallback to other directed broadcast */ }
        }
        var nodes = new Dictionary<string, VehicleNode>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(duration);
        while (true)
        {
            try
            {
                var packet = await client.ReceiveAsync(deadline.Token);
                var frame = DoIpCodec.Decode(packet.Buffer);
                if (frame.Type != DoIpCodec.VehicleAnnouncement) continue;
                var vehicle = DoIpCodec.ParseVehicle(frame.Payload, packet.RemoteEndPoint.Address.ToString());
                nodes[vehicle.Ip + ":" + vehicle.LogicalAddress] = vehicle;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { break; }
            catch (FormatException) { /* ignore unrelated/malformed replies */ }
            catch (SocketException) { break; }
        }
        token.ThrowIfCancellationRequested();
        return nodes.Values.ToList();
    }
}

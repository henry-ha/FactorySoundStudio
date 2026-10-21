using System;
using System.Buffers.Binary;
using System.Text;

namespace FactorySoundStudio.Core;

internal readonly record struct DoIpFrame(byte Version, ushort Type, byte[] Payload);

internal sealed record VehicleNode(string Ip, ushort LogicalAddress, string Vin, string Eid, string Gid, byte FurtherAction)
{
    public string MaskedVin => Privacy.MaskVin(Vin);
    public override string ToString() => $"{Ip}  |  0x{LogicalAddress:X4}  |  VIN {MaskedVin}";
}

internal static class DoIpCodec
{
    public const ushort DiscoveryRequest = 0x0001;
    public const ushort VehicleAnnouncement = 0x0004;
    public const ushort RoutingRequest = 0x0005;
    public const ushort RoutingResponse = 0x0006;
    public const ushort AliveCheckRequest = 0x0007;
    public const ushort AliveCheckResponse = 0x0008;
    public const ushort DiagnosticMessage = 0x8001;
    public const ushort DiagnosticAck = 0x8002;
    public const ushort DiagnosticNack = 0x8003;
    public const int MaxPacketBytes = 128 * 1024;

    public static byte[] Encode(ushort type, ReadOnlySpan<byte> payload, byte version = 0x02)
    {
        if (version != 0x02 && version != 0x03) throw new ArgumentException("Unsupported DoIP version.");
        if (payload.Length > MaxPacketBytes) throw new ArgumentException("Packet too large.");
        var data = new byte[8 + payload.Length];
        data[0] = version;
        data[1] = (byte)~version;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2, 2), type);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), (uint)payload.Length);
        payload.CopyTo(data.AsSpan(8));
        return data;
    }

    public static DoIpFrame Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8) throw new FormatException("DoIP header incomplete.");
        byte version = bytes[0];
        if ((version != 0x02 && version != 0x03) || bytes[1] != (byte)~version)
            throw new FormatException("Invalid or unsupported DoIP version/inverse version.");
        var type = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(2, 2));
        var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(4, 4));
        if (length > MaxPacketBytes || length != (uint)(bytes.Length - 8))
            throw new FormatException("DoIP payload length invalid.");
        return new DoIpFrame(version, type, bytes.Slice(8).ToArray());
    }

    public static VehicleNode ParseVehicle(ReadOnlySpan<byte> payload, string senderIp)
    {
        if (payload.Length != 32 && payload.Length != 33)
            throw new FormatException("Unexpected vehicle announcement size.");
        string vin = Encoding.ASCII.GetString(payload.Slice(0, 17)).Trim('\0', ' ', '\u00ff');
        ushort address = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(17, 2));
        string eid = Convert.ToHexString(payload.Slice(19, 6));
        string gid = Convert.ToHexString(payload.Slice(25, 6));
        return new VehicleNode(senderIp, address, vin, eid, gid, payload[31]);
    }

    public static byte[] RoutingActivation(ushort testerAddress)
    {
        // ISO 13400-2 default activation: 2-byte tester LA, type 0, four reserved bytes.
        byte[] payload = new byte[7];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), testerAddress);
        return payload;
    }

    public static byte[] Diagnostic(ushort source, ushort target, ReadOnlySpan<byte> uds)
    {
        byte[] payload = new byte[4 + uds.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), source);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), target);
        uds.CopyTo(payload.AsSpan(4));
        return payload;
    }

    public static ushort ReadUInt16(ReadOnlySpan<byte> input, int offset)
    {
        if (input.Length < offset + 2) throw new FormatException("Short DoIP field.");
        return BinaryPrimitives.ReadUInt16BigEndian(input.Slice(offset, 2));
    }
}

internal static class Privacy
{
    public static string MaskVin(string vin)
    {
        if (string.IsNullOrWhiteSpace(vin)) return "unavailable";
        if (vin.Length <= 6) return "******";
        return vin.Substring(0, 3) + new string('*', vin.Length - 6) + vin.Substring(vin.Length - 3);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FactorySoundStudio.Core;

internal static class SafeDid
{
    // Hardcoded allowlist of UDS ReadDataByIdentifier identifiers. No generic transmit API exists.
    public static readonly IReadOnlyDictionary<ushort, string> Identifiers = new Dictionary<ushort, string>
    {
        [0xF187] = "Manufacturer spare-part number",
        [0xF188] = "Manufacturer ECU software number",
        [0xF189] = "Manufacturer ECU software version",
        [0xF18A] = "System supplier",
        [0xF190] = "VIN (masked)",
        [0xF191] = "Manufacturer ECU hardware number"
    };

    public static byte[] Request(ushort did)
    {
        if (!Identifiers.ContainsKey(did)) throw new InvalidOperationException("DID is not in the read-only allowlist.");
        return new byte[] { 0x22, (byte)(did >> 8), (byte)did };
    }

    public static string DescribeResponse(ushort did, ReadOnlySpan<byte> uds)
    {
        if (!Identifiers.ContainsKey(did)) throw new InvalidOperationException("DID not permitted.");
        if (uds.Length >= 3 && uds[0] == 0x7F && uds[1] == 0x22)
            return $"Unavailable: NRC 0x{uds[2]:X2} ({NrcDescription(uds[2])})";
        if (uds.Length < 3 || uds[0] != 0x62 || uds[1] != (byte)(did >> 8) || uds[2] != (byte)did)
            return "Unexpected response";
        var body = uds.Slice(3);
        if (body.IsEmpty) return "Empty value";
        // Avoid leaking a VIN or other identifier in user-exported session reports.
        if (did == 0xF190)
            return Privacy.MaskVin(Encoding.ASCII.GetString(body).Trim('\0', ' '));
        bool readable = body.ToArray().All(b => b == 0 || b == 0x20 || (b >= 0x21 && b <= 0x7E));
        string value = readable ? Encoding.ASCII.GetString(body).Trim('\0', ' ') : "HEX " + Convert.ToHexString(body);
        return value.Length <= 160 ? value : value[..160] + "…";
    }

    public static string NrcDescription(byte nrc) => nrc switch
    {
        0x11 => "service not supported",
        0x12 => "sub-function not supported",
        0x13 => "incorrect length or format",
        0x21 => "busy; retry later",
        0x22 => "conditions not correct",
        0x31 => "request out of range / DID unsupported",
        0x33 => "security access denied",
        0x35 => "invalid key",
        0x78 => "response pending",
        0x7E => "sub-function not supported in active session",
        0x7F => "service not supported in active session",
        _ => "ECU rejected request"
    };
}

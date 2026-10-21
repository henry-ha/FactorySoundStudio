using System;
using System.Linq;
using System.Text;
using FactorySoundStudio.Core;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    passed++;
    Console.WriteLine("PASS " + name);
}
var discover = DoIpCodec.Encode(0x0001, Array.Empty<byte>());
Check(Convert.ToHexString(discover) == "02FD000100000000", "wire-format discovery request");
var pkt = DoIpCodec.Decode(discover);
Check(pkt.Version == 2 && pkt.Type == 1 && pkt.Payload.Length == 0, "roundtrip frame");
byte[] payload = new byte[33];
Encoding.ASCII.GetBytes("WAUZZZ00000000001").CopyTo(payload, 0);
payload[17] = 0x10; payload[18] = 0x01; payload[31] = 0;
var announce = DoIpCodec.ParseVehicle(payload, "169.254.1.2");
Check(announce.LogicalAddress == 0x1001, "big-endian logical address");
Check(!announce.MaskedVin.Contains("00000000001"), "masked VIN");
Check(Convert.ToHexString(DoIpCodec.RoutingActivation(0x0E00)) == "0E000000000000", "default routing activation");
Check(Convert.ToHexString(DoIpCodec.Diagnostic(0x0E00, 0x1001, SafeDid.Request(0xF190))) == "0E00100122F190", "read-only UDS payload");
Check(SafeDid.DescribeResponse(0xF187, new byte[] { 0x62, 0xF1, 0x87, 0x54, 0x45, 0x53, 0x54 }) == "TEST", "response decode");
Check(SafeDid.DescribeResponse(0xF187, new byte[] { 0x7F, 0x22, 0x31 }).Contains("DID unsupported"), "negative response handling");
try { SafeDid.Request(0xF199); throw new Exception("Unsupported DID wasn't rejected."); }
catch (InvalidOperationException) { Check(true, "DID allowlist enforcement"); }
try { DoIpCodec.Decode(new byte[] { 0x02, 0xFD, 0, 1, 0, 0, 0, 0, 0 }); throw new Exception("Invalid length wasn't rejected."); }
catch (FormatException) { Check(true, "reject malformed length"); }
Console.WriteLine($"PASS: {passed} self-checks completed.");

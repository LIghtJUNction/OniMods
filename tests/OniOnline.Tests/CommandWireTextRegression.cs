using System;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Multiplayer;

internal static class CommandWireTextRegression
{
    internal static void Run(Action<bool, string> check)
    {
        CheckPacket(check, "work_text", target => RemoteWorkWire.Encode(new RemoteWorkMessage
        {
            Kind = RemoteWorkMessageKind.QueryPlans, TargetId = target, PlanetId = "p"
        }), bytes => RemoteWorkWire.Decode(bytes).TargetId, "T05JUgQEAQBBAQBwAAAA");
        CheckPacket(check, "dupe_text", target => DupeCommandWire.Encode(new DupeCommandMessage
        {
            TargetId = target,
            Receipt = new DupeCommandReceipt { RequestId = new string('0', 32), Reason = "" }
        }), bytes => DupeCommandWire.Decode(bytes).TargetId,
            "T05JRAICAQBBIAAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMAAAAAAAAQ==");
    }

    private static void CheckPacket(Action<bool, string> check, string error,
        Func<string, byte[]> encode, Func<byte[], string> decode, string fixture)
    {
        byte[] packet = encode("A");
        check(Convert.ToBase64String(packet) == fixture, error + " preserves its published packet bytes");
        foreach (string value in new[] { "", "A", "\0\r\n", new string('a', 128), new string('界', 128) })
            check(decode(encode(value)) == value, error + " round trips empty, control, ASCII and multibyte text");

        byte[] maximum = encode(new string('界', 128));
        check(maximum[6] == 128 && maximum[7] == 1,
            error + " prefixes the 384-byte UTF-8 boundary with a little-endian ushort");
        Expect<InvalidDataException>(check, () => encode(null), error, "null text");
        Expect<InvalidDataException>(check, () => encode(new string('a', 129)), error, "oversized text");
        Expect<EncoderFallbackException>(check, () => encode("\ud800"), null, "unpaired surrogate");
        Expect<DecoderFallbackException>(check, () => decode(ReplaceTarget(packet, new byte[] { 255 })),
            null, "malformed UTF-8");
        Expect<InvalidDataException>(check, () => decode(packet.Take(8).ToArray()), error, "truncated text");
        byte[] oversized = (byte[])packet.Clone();
        oversized[6] = 129;
        oversized[7] = 1;
        Expect<InvalidDataException>(check, () => decode(oversized), error, "oversized byte prefix");
        Expect<InvalidDataException>(check,
            () => decode(ReplaceTarget(packet, Enumerable.Repeat((byte)'a', 129).ToArray())),
            error, "decoded character overflow");
    }

    private static byte[] ReplaceTarget(byte[] packet, byte[] target)
    {
        int previousLength = packet[6] | packet[7] << 8;
        return packet.Take(6).Concat(new[] { (byte)target.Length, (byte)(target.Length >> 8) })
            .Concat(target).Concat(packet.Skip(8 + previousLength)).ToArray();
    }

    private static void Expect<T>(Action<bool, string> check, Action action, string message, string scenario)
        where T : Exception
    {
        Exception failure = null;
        try { action(); }
        catch (Exception error) { failure = error; }
        check(failure is T && (message == null || failure.Message == message),
            scenario + " preserves " + typeof(T).Name + (message == null ? "" : ": " + message));
    }
}

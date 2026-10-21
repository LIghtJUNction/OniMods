using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Core;

internal static class NativeSimSaveBlockTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] original = Fixture(6, 4, 7, 9, 5);
        var block = NativeSimSaveBlock.Read(original);
        check(block.GameWidth == 6 && block.GameHeight == 4 && block.StorageWidth == 8 &&
            block.StorageHeight == 6 && block.TargetX == 7 && block.TargetY == 9 && block.Settings == 5,
            "native block distinguishes logical dimensions, halo, offset and settings");

        original[33] ^= 127;
        byte[] writableCopy = block.CopySerializedBytes();
        writableCopy[34] ^= 63;
        check(block.CopySerializedBytes().SequenceEqual(Fixture(6, 4, 7, 9, 5)),
            "native block owns its checked bytes and does not expose a mutable buffer");

        var crop = block.Crop(2, 1, 3, 2);
        check(crop.GameWidth == 3 && crop.GameHeight == 2 && crop.TargetX == 0 && crop.TargetY == 0 && crop.Settings == 5,
            "selected planet crop has its own dimensions and neutral target");
        byte[] expected = Fixture(3, 2, 0, 0, 5, 2, 1);
        check(crop.CopySerializedBytes().SequenceEqual(expected),
            "selected planet keeps every field in all three native record arrays, including the original halo");
        check(block.Crop(1, 0, 5, 4).Crop(1, 1, 3, 2).CopySerializedBytes().SequenceEqual(expected),
            "logical crop coordinates compose without a one-cell shift");
        check(block.Crop(0, 0, 6, 4).CopySerializedBytes().SequenceEqual(Fixture(6, 4, 0, 0, 5)),
            "full extent crop preserves all records and only normalizes target offset");
        check(block.Crop(5, 3, 1, 1).CopySerializedBytes().SequenceEqual(Fixture(1, 1, 0, 0, 5, 5, 3)),
            "last logical cell retains a complete one-cell halo on all four sides");

        byte[] placed = crop.SerializeAt(4, 6, 7, 8, 5);
        var placement = NativeSimSaveBlock.Read(placed);
        check(placement.TargetX == 4 && placement.TargetY == 6 &&
            placed.Skip(33).SequenceEqual(expected.Skip(33)) && crop.TargetX == 0 && crop.TargetY == 0,
            "placing at the exact destination edge changes only target offset, not source records");

        check(Rejected(() => crop.SerializeAt(4, 6, 7, 8, 4)), "incompatible global native settings rejected");
        check(Rejected(() => crop.SerializeAt(5, 6, 7, 8, 5)), "right destination overflow rejected");
        check(Rejected(() => crop.SerializeAt(4, 7, 7, 8, 5)), "top destination overflow rejected");
        check(Rejected(() => crop.SerializeAt(-1, 0, 7, 8, 5)), "negative destination offset rejected");
        check(Rejected(() => crop.SerializeAt(0, 0, int.MaxValue, 8, 5)), "destination dimension arithmetic cannot wrap");
        check(Rejected(() => block.Crop(6, 0, 1, 1)), "crop cannot select a storage halo as a game cell");
        check(Rejected(() => block.Crop(0, 4, 1, 1)), "crop cannot extend beyond the logical top edge");
        check(Rejected(() => block.Crop(1, 0, int.MaxValue, 1)), "crop extent arithmetic cannot wrap");
        check(Rejected(() => block.Crop(0, 0, 0, 1)), "empty crop rejected");

        byte[] valid = Fixture(6, 4, 0, 0, 5);
        check(Rejected(() => NativeSimSaveBlock.Read(valid.Take(valid.Length - 1).ToArray())), "truncated block rejected");
        check(Rejected(() => NativeSimSaveBlock.Read(valid.Concat(new byte[] { 0 }).ToArray())), "trailing bytes rejected");
        CheckHeaderRejection(check, valid, 0, valid.Length, "incorrect managed length rejected");
        CheckHeaderRejection(check, valid, 4, 0, "incorrect magic rejected");
        CheckHeaderRejection(check, valid, 12, 14, "unsupported native format rejected");
        CheckHeaderRejection(check, valid, 16, 2, "missing native halo rejected");
        CheckHeaderRejection(check, valid, 16, int.MaxValue, "oversized storage width rejected before record allocation");
        CheckHeaderRejection(check, valid, 20, -1, "negative storage height rejected");
        CheckHeaderRejection(check, valid, 24, -1, "negative stored offset rejected");
        CheckHeaderRejection(check, valid, 28, int.MaxValue, "stored offset arithmetic cannot wrap");
        var excessive = (byte[])valid.Clone();
        Put(excessive, 16, 2000); Put(excessive, 20, 2000);
        check(Rejected(() => NativeSimSaveBlock.Read(excessive)), "storage cell budget checked before trusting body size");
        check(Rejected(() => NativeSimSaveBlock.Read(new byte[32])), "incomplete header rejected");
        bool nullRejected = false;
        try { NativeSimSaveBlock.Read(null); } catch (ArgumentNullException) { nullRejected = true; }
        check(nullRejected, "missing block rejected");
        Console.WriteLine("Native Sim block: logical crop, original halo and opaque persistent record preservation; host execution only.");
    }

    private static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
        catch (OverflowException) { return true; }
    }

    private static void CheckHeaderRejection(Action<bool, string> check, byte[] source, int offset, int value, string description)
    {
        var changed = (byte[])source.Clone(); Put(changed, offset, value);
        check(Rejected(() => NativeSimSaveBlock.Read(changed)), description);
    }

    private static void Put(byte[] bytes, int offset, int value)
    {
        using (var stream = new MemoryStream(bytes))
        using (var writer = new BinaryWriter(stream))
        { stream.Position = offset; writer.Write(value); }
    }

    // Each semantic field carries a coordinate-specific value, so a wrong row,
    // channel base, stride or halo offset changes the expected output.
    private static byte[] Fixture(int gameWidth, int gameHeight, int targetX, int targetY, byte settings,
        int coordinateX = 0, int coordinateY = 0)
    {
        int width = gameWidth + 2, height = gameHeight + 2;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(29 + 36 * width * height);
            writer.Write(Encoding.ASCII.GetBytes("SIMSAVE\0")); writer.Write(15);
            writer.Write(width); writer.Write(height); writer.Write(targetX); writer.Write(targetY); writer.Write(settings);
            foreach (var cell in Coordinates(width, height, coordinateX, coordinateY))
            { writer.Write(cell); writer.Write(cell + 0.125f); writer.Write(cell + 0.25f); writer.Write(cell + 0.5f); }
            foreach (var cell in Coordinates(width, height, coordinateX, coordinateY))
            { writer.Write(100000 + cell); writer.Write(200000 + cell); }
            foreach (var cell in Coordinates(width, height, coordinateX, coordinateY))
            { writer.Write(300000 + cell); writer.Write(cell + 0.75f); writer.Write(cell + 0.875f); }
            return stream.ToArray();
        }
    }

    private static IEnumerable<int> Coordinates(int width, int height, int x, int y)
    {
        for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
                yield return (y + row) * 100 + x + column;
    }
}

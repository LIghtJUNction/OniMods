using System;
using OniMcp;

internal static class CoordinateLabelPlacementRegression
{
    internal static void Run()
    {
        // The requested 60-cell rectangle can extend beyond all four viewport edges
        // when focusCamera=false. Keep visible axis coordinates readable inside it.
        CheckPlacement(600f, -500f, 1280f, 800f, 42f, 20f, 579f, 4f);
        CheckPlacement(600f, 1300f, 1280f, 800f, 42f, 20f, 579f, 776f);
        CheckPlacement(400f, -600f, 800f, 1280f, 20f, 40f, 390f, 4f);
        CheckPlacement(400f, 1800f, 800f, 1280f, 20f, 40f, 390f, 1236f);
        CheckPlacement(600f, 100f, 1280f, 800f, 42f, 20f, 579f, 100f);

        foreach (float position in new[] { 0f, 1f, 639f, 640f })
        {
            float along, across;
            Require(CoordinateLabelPlacement.TryPlace(position, -300f, 640f, 480f,
                42f, 20f, out along, out across), "visible coordinate was dropped");
            Require(along >= 0f && along + 42f <= 640f
                && across >= 0f && across + 20f <= 480f,
                "label rectangle extends beyond the screenshot");
        }

        foreach (float position in new[] { -1f, 641f, float.NaN, float.PositiveInfinity })
        {
            float along, across;
            Require(!CoordinateLabelPlacement.TryPlace(position, 100f, 640f, 480f,
                42f, 20f, out along, out across),
                "offscreen coordinates must not pile up on a visible screen edge");
        }
        float x, y;
        Require(!CoordinateLabelPlacement.TryPlace(10f, 0f, 20f, 10f,
            42f, 20f, out x, out y), "a label cannot fit in a smaller viewport");
    }

    private static void CheckPlacement(float position, float edge, float length, float depth,
        float labelLength, float labelDepth, float expectedAlong, float expectedAcross)
    {
        float along, across;
        Require(CoordinateLabelPlacement.TryPlace(position, edge, length, depth,
            labelLength, labelDepth, out along, out across), "visible axis label missing");
        Require(along == expectedAlong && across == expectedAcross,
            "axis label was not constrained to the visible viewport");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

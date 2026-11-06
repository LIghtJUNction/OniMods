using System;

namespace OniMcp.Tools
{
    internal sealed class BuildPlacementIdentityResult
    {
        public bool AnchorMatches;
        public bool WorldMatches;
        public bool OrientationMatches;
        public bool Valid => AnchorMatches && WorldMatches && OrientationMatches;
    }

    internal static class BuildPlacementIdentityPolicy
    {
        internal static BuildPlacementIdentityResult Evaluate(
            int expectedX,
            int expectedY,
            int expectedWorldId,
            string expectedOrientation,
            int actualX,
            int actualY,
            int actualWorldId,
            string actualOrientation,
            bool orientationRelevant)
        {
            string expected = NormalizeOrientation(expectedOrientation);
            string actual = NormalizeOrientation(actualOrientation);
            return new BuildPlacementIdentityResult
            {
                AnchorMatches = actualX == expectedX && actualY == expectedY,
                WorldMatches = actualWorldId < 0 || expectedWorldId < 0 || actualWorldId == expectedWorldId,
                OrientationMatches = !orientationRelevant
                    || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string NormalizeOrientation(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Neutral" : value.Trim();
        }
    }
}

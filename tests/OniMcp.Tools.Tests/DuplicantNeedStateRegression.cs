using System;
using OniMcp.Tools;

internal static class DuplicantNeedStateRegression
{
    internal static void Run()
    {
        Check(DuplicantNeedStatePolicy.Availability(false, true, true, true) == "known",
            "native amount values are marked known");
        Check(DuplicantNeedStatePolicy.Availability(false, true, true, false) == "unknown",
            "missing organic amount data remains unknown");
        Check(DuplicantNeedStatePolicy.Availability(true, true, true, false) == "notApplicable",
            "missing bionic-only model needs are marked notApplicable");
        Check(DuplicantNeedStatePolicy.Availability(true, false, true, false) == "unknown",
            "missing amount component is unknown even for a bionic duplicant");
        Check(DuplicantNeedStatePolicy.Availability(true, true, false, false) == "unknown",
            "a missing native database definition is unknown rather than notApplicable");
        Check(DuplicantNeedStatePolicy.WaterRisk(true, 0) == "noneObserved",
            "a completed local scan with no water-exposed powerbanks is distinguished from unknown");
        Check(DuplicantNeedStatePolicy.WaterRisk(true, 1) == "risk",
            "a water-exposed loose powerbank reports damage risk");
        Check(DuplicantNeedStatePolicy.WaterRisk(false, 0) == "unknown",
            "an incomplete scan never reports no risk");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

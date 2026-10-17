using System;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static partial class Program
    {
        private static void VerifyFetchPatchActivationPolicy()
        {
            if (!FetchPatchActivationPolicy.AllowsCycleTrimReplacement(
                    efficientSupplyPresent: false))
            {
                throw new InvalidOperationException(
                    "CycleTrim-only fetch optimization must stay enabled");
            }

            if (FetchPatchActivationPolicy.AllowsCycleTrimReplacement(
                    efficientSupplyPresent: true))
            {
                throw new InvalidOperationException(
                    "Efficient Supply presence must disable CycleTrim fetch replacement");
            }

            if (FetchPatchActivationPolicy.AllowsCycleTrimReplacement(
                    efficientSupplyPresent: false,
                    bundledEfficientFetchPresent: true))
            {
                throw new InvalidOperationException(
                    "bundled Efficient Fetch presence must disable CycleTrim fetch replacement");
            }
        }
    }
}

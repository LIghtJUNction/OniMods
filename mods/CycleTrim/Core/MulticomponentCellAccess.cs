using System;
using System.Threading;

namespace CycleTrim.Core
{
    public enum MulticomponentPhase : byte
    {
        Gas = 0,
        Liquid = 1,
        Solid = 2
    }

    /// <summary>
    /// Read-only species data for one cell. Element must be the game's canonical
    /// Element instance; values use ONI's mass, temperature and pressure units.
    /// </summary>
    public struct MulticomponentSpecies
    {
        public readonly Element Element;
        public readonly MulticomponentPhase Phase;
        public readonly float Mass;
        public readonly float Temperature;
        public readonly float VolumeFraction;
        public readonly float PartialPressureKPa;

        public MulticomponentSpecies(Element element, MulticomponentPhase phase,
            float mass, float temperature, float volumeFraction, float partialPressureKPa)
        {
            Element = element;
            Phase = phase;
            Mass = mass;
            Temperature = temperature;
            VolumeFraction = volumeFraction;
            PartialPressureKPa = partialPressureKPa;
        }
    }

    /// <summary>
    /// Borrowed per-cell snapshot. The provider owns Components and must keep it
    /// stable for the duration of the synchronous TryGetCell call chain.
    /// </summary>
    public struct MulticomponentCellSnapshot
    {
        public readonly MulticomponentSpecies[] Components;
        public readonly int ComponentCount;
        public readonly float TotalMass;
        public readonly float GasPressureKPa;
        public readonly float LiquidPressureKPa;
        public readonly float LiquidVolumeFraction;

        public MulticomponentCellSnapshot(MulticomponentSpecies[] components,
            int componentCount, float totalMass, float gasPressureKPa, float liquidPressureKPa,
            float liquidVolumeFraction)
        {
            Components = components;
            ComponentCount = componentCount;
            TotalMass = totalMass;
            GasPressureKPa = gasPressureKPa;
            LiquidPressureKPa = liquidPressureKPa;
            LiquidVolumeFraction = liquidVolumeFraction;
        }
    }

    /// <summary>
    /// Implemented by the OniSim managed/native bridge when per-cell species
    /// data is actually published. The current legacy Grid ABI is not a provider.
    /// </summary>
    public interface IMulticomponentCellProvider
    {
        bool IsReady { get; }
        bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot);
    }

    /// <summary>
    /// Fail-closed access point shared by all Harmony shims. With no ready
    /// provider, callers must leave the original game method untouched.
    /// </summary>
    public static class MulticomponentCellAccess
    {
        private static IMulticomponentCellProvider provider;
        private static int providerFailureReported;

        public static bool RegisterProvider(IMulticomponentCellProvider value)
        {
            if (value == null)
                return false;
            return Interlocked.CompareExchange(ref provider, value, null) == null;
        }

        public static bool UnregisterProvider(IMulticomponentCellProvider value)
        {
            if (value == null)
                return false;
            return ReferenceEquals(Interlocked.CompareExchange(ref provider, null, value), value);
        }

        public static bool IsReady
        {
            get
            {
                IMulticomponentCellProvider current = Volatile.Read(ref provider);
                try { return current != null && current.IsReady; }
                catch (Exception error)
                {
                    ReportProviderFailure(error);
                    return false;
                }
            }
        }

        public static bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot)
        {
            snapshot = default(MulticomponentCellSnapshot);
            if (!CycleTrimPatchOptions.EnableMulticomponentShim)
                return false;

            IMulticomponentCellProvider current = Volatile.Read(ref provider);
            if (current == null)
                return false;

            try
            {
                if (!current.TryGetCell(cell, out snapshot) || !IsValid(snapshot))
                {
                    snapshot = default(MulticomponentCellSnapshot);
                    return false;
                }
                return true;
            }
            catch (Exception error)
            {
                snapshot = default(MulticomponentCellSnapshot);
                ReportProviderFailure(error);
                return false;
            }
        }

        private static bool IsValid(MulticomponentCellSnapshot snapshot)
        {
            if (snapshot.Components == null || snapshot.ComponentCount < 0
                || snapshot.ComponentCount > snapshot.Components.Length || !FiniteNonNegative(snapshot.TotalMass)
                || !FiniteNonNegative(snapshot.GasPressureKPa)
                || !FiniteNonNegative(snapshot.LiquidPressureKPa)
                || !FiniteNonNegative(snapshot.LiquidVolumeFraction)
                || snapshot.LiquidVolumeFraction > 1f)
                return false;

            float mass = 0f;
            for (int i = 0; i < snapshot.ComponentCount; i++)
            {
                MulticomponentSpecies item = snapshot.Components[i];
                if (item.Element == null || !FiniteNonNegative(item.Mass)
                    || !Finite(item.Temperature) || !FiniteNonNegative(item.VolumeFraction)
                    || item.VolumeFraction > 1f || !FiniteNonNegative(item.PartialPressureKPa))
                    return false;
                mass += item.Mass;
                if (!FiniteNonNegative(mass))
                    return false;
            }

            // The provider must publish a self-consistent mass aggregate.
            float tolerance = Math.Max(0.001f, snapshot.TotalMass * 0.0001f);
            return Math.Abs(mass - snapshot.TotalMass) <= tolerance;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool FiniteNonNegative(float value)
        {
            return Finite(value) && value >= 0f;
        }

        private static void ReportProviderFailure(Exception error)
        {
            if (Interlocked.Exchange(ref providerFailureReported, 1) == 0)
                Console.WriteLine("[CycleTrim] Multicomponent provider failed; retaining original game behavior: " + error);
        }
    }
}

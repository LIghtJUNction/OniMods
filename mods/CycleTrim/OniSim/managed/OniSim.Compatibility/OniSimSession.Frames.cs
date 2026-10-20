using System;
using System.Collections.Generic;

namespace OniSim.Compatibility
{
    public sealed partial class OniSimSession
    {
        /// <summary>Submits official active-region records; it does not publish Grid data.</summary>
        public void NewFrame(float elapsedSeconds, IList<ActiveRegion> activeRegions)
        {
            RejectReentry();
            if (activeRegions == null) throw new ArgumentNullException("activeRegions");
            int count = activeRegions.Count;
            if (count < 0 || count > BinaryLayout.MaxPayloadBytes / 28)
                throw new ArgumentOutOfRangeException("activeRegions");
            var payload = new byte[checked(count * 28)];
            for (int i = 0; i < count; i++)
            {
                ActiveRegion region = activeRegions[i];
                int offset = i * 28;
                BinaryLayout.WriteF32(payload, offset, elapsedSeconds);
                BinaryLayout.WriteI32(payload, offset + 4, region.MinX);
                BinaryLayout.WriteI32(payload, offset + 8, region.MinY);
                BinaryLayout.WriteI32(payload, offset + 12, region.MaxX);
                BinaryLayout.WriteI32(payload, offset + 16, region.MaxY);
                BinaryLayout.WriteF32(payload, offset + 20, region.SunlightIntensity);
                BinaryLayout.WriteF32(payload, offset + 24, region.CosmicRadiationIntensity);
            }
            Locked(() => { SendCore(new OfficialMessage(OfficialMessages.NewFrame, payload, true), 0); return true; });
        }

        /// <summary>Performs the native publication handoff using raw per-cell visibility bytes.</summary>
        public GridView Prepare(byte[] visibility)
        {
            RejectReentry();
            var message = new OfficialMessage(OfficialMessages.Prepare, visibility);
            return Locked(() =>
            {
                SendCore(message, 0);
                return NewView();
            });
        }
    }

    /// <summary>Official NewGameFrame public-grid coordinates and environmental inputs.</summary>
    public readonly struct ActiveRegion
    {
        public int MinX { get; }
        public int MinY { get; }
        public int MaxX { get; }
        public int MaxY { get; }
        public float SunlightIntensity { get; }
        public float CosmicRadiationIntensity { get; }

        public ActiveRegion(int minX, int minY, int maxX, int maxY,
            float sunlightIntensity = 0, float cosmicRadiationIntensity = 0)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            SunlightIntensity = sunlightIntensity; CosmicRadiationIntensity = cosmicRadiationIntensity;
        }
    }
}

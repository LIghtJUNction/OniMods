using System;

namespace OniSim.Compatibility
{
    public sealed class OfficialMessage
    {
        private readonly byte[] payload;
        public int Id { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }
        internal byte[] OwnedPayload { get { return payload; } }

        public OfficialMessage(int id, byte[] payload) : this(id, payload, false) { }
        internal OfficialMessage(int id, byte[] payload, bool takeOwnership)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            if (payload.Length > BinaryLayout.MaxPayloadBytes) throw new ArgumentOutOfRangeException("payload");
            Id = id;
            this.payload = takeOwnership ? payload : (byte[])payload.Clone();
        }
    }

    public sealed class BatchResult
    {
        public uint ExecutedRecords { get; private set; }
        internal BatchResult(uint executed) { ExecutedRecords = executed; }
    }

    public sealed class OniSimOperationException : InvalidOperationException
    {
        public int Status { get; private set; }
        public uint ExecutedRecords { get; private set; }
        public string NativeErrorText { get; private set; }

        internal OniSimOperationException(int status, uint executed, string message,
            string nativeErrorText, Exception inner = null)
            : base(message + " Native status=" + status + ", executed records=" + executed + "." +
                (string.IsNullOrEmpty(nativeErrorText) ? "" : " Native error: " + nativeErrorText), inner)
        { Status = status; ExecutedRecords = executed; NativeErrorText = nativeErrorText; }
    }

    internal static class OfficialMessages
    {
        internal const int Allocate = 1092408308;
        internal const int Initialize = 2062421945;
        internal const int Start = -931446686;
        internal const int Modify = -1252920804;
        internal const int ElementTable = 1108437482;
        internal const int DiseaseTable = 825301935;
        internal const int NewFrame = -775326397;
        internal const int Prepare = 1078620451;
        internal const int Load = -672538170;

        // The native endpoint owns message IDs, wire formats, and state validation.
        // Generic dispatch checks only the managed buffer's ownership and size.
        internal static void Validate(OfficialMessage message)
        {
            if (message == null) throw new ArgumentException("Message must not be null.", "message");
            if (message.OwnedPayload.Length > BinaryLayout.MaxPayloadBytes)
                throw new ArgumentOutOfRangeException("message", "Message exceeds the managed buffer limit.");
        }

        internal static void ValidateDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0 || (long)width * height > BinaryLayout.MaxCells)
                throw new ArgumentOutOfRangeException("width", "Grid dimensions are outside the supported limit.");
        }

        internal static void ValidateCellValues(float temperature, float mass)
        {
            if (!GridImage.IsFinite(temperature) || temperature < 0 || !GridImage.IsFinite(mass) || mass < 0)
                throw new ArgumentOutOfRangeException("temperature", "Cell values must be finite and nonnegative.");
        }

    }
}

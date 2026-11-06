using System;

namespace OniSim.Compatibility
{
    /// <summary>Borrowed native Grid descriptor; the arrays are owned by the native core.</summary>
    public sealed class GridView
    {
        public const int DescriptorBytes = 496;
        private readonly OniSimSession owner;
        private readonly ulong generation;

        internal GridView(OniSimSession session, ulong value)
        { owner = session; generation = value; }

        /// <summary>Copies only the official 496-byte descriptor, never the Grid arrays.</summary>
        public byte[] GetDescriptor() { return owner.GetDescriptor(generation); }

        /// <summary>Borrowed native descriptor pointer; do not use beyond this grid generation.</summary>
        public IntPtr DescriptorPointer { get { return owner.GetGridPointer(generation); } }

        public ulong Generation
        { get { owner.ValidateView(generation); return generation; } }
    }
}

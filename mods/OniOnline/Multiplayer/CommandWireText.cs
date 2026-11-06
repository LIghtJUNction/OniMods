using System.IO;
using System.Text;

namespace OniOnline.Multiplayer
{
    internal sealed class CommandWireText
    {
        private const int MaxCharacters = 128;
        private const int MaxBytes = 384;
        internal static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);
        private readonly string error;

        internal CommandWireText(string error)
        {
            this.error = error;
        }

        internal void Write(BinaryWriter writer, string value)
        {
            if (value == null || value.Length > MaxCharacters) throw new InvalidDataException(error);
            byte[] bytes = Encoding.GetBytes(value);
            if (bytes.Length > MaxBytes) throw new InvalidDataException(error);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        internal string Read(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length > MaxBytes || reader.BaseStream.Length - reader.BaseStream.Position < length)
                throw new InvalidDataException(error);
            string value = Encoding.GetString(reader.ReadBytes(length));
            if (value.Length > MaxCharacters) throw new InvalidDataException(error);
            return value;
        }
    }
}

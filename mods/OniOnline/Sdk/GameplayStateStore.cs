using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OniOnline.Sdk
{
    public sealed class GameplayState
    {
        public int Schema { get; }
        public byte[] Payload { get; }
        internal GameplayState(int schema, byte[] payload) { Schema = schema; Payload = (byte[])payload.Clone(); }
    }
    // Per native save, not global profile state. Unknown/invalid containers stay intact and read-only.
    public sealed class GameplayStateStore
    {
        public const int MaxSlotBytes = 16384, MaxSlots = 16;
        private readonly Dictionary<string, GameplayState> slots = new Dictionary<string, GameplayState>(StringComparer.Ordinal);
        private readonly int originalSchema;
        private readonly string originalPayload;
        public bool Blocked { get; private set; }
        public int Schema { get { return Blocked ? originalSchema : 1; } }
        private GameplayStateStore(int schema, string payload) { originalSchema = schema; originalPayload = payload; }
        public static GameplayStateStore Load(int schema, string payload)
        {
            var store = new GameplayStateStore(schema, payload);
            if (schema == 0 && payload == null) return store;
            try
            {
                if (schema != 1 || payload == null || payload.Length > (MaxSlotBytes + 160) * MaxSlots * 2) throw new InvalidDataException();
                byte[] bytes = Convert.FromBase64String(payload);
                using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
                {
                    int count = reader.ReadInt32(); if (count < 0 || count > MaxSlots) throw new InvalidDataException();
                    for (int i = 0; i < count; i++)
                    {
                        string id = reader.ReadString(); int version = reader.ReadInt32(), length = reader.ReadInt32();
                        if (!GameplayFramework.ValidName(id) || version < 1 || length < 0 || length > MaxSlotBytes || store.slots.ContainsKey(id)) throw new InvalidDataException();
                        byte[] data = reader.ReadBytes(length); if (data.Length != length) throw new EndOfStreamException();
                        store.slots.Add(id, new GameplayState(version, data));
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException();
                }
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is FormatException)
            { store.slots.Clear(); store.Blocked = true; }
            return store;
        }
        public GameplayState Read(string id)
        { return !Blocked && id != null && slots.TryGetValue(id, out var state) ? new GameplayState(state.Schema, state.Payload) : null; }
        public void Write(string id, int schema, byte[] payload)
        {
            if (Blocked) throw new InvalidOperationException("gameplay_save_blocked");
            if (!GameplayFramework.ValidName(id) || schema < 1 || payload == null || payload.Length > MaxSlotBytes) throw new ArgumentException("invalid_gameplay_state");
            if (!slots.ContainsKey(id) && slots.Count >= MaxSlots) throw new InvalidOperationException("gameplay_save_budget");
            slots[id] = new GameplayState(schema, payload);
        }
        public string Capture()
        {
            if (Blocked) return originalPayload;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true)))
            {
                writer.Write(slots.Count);
                foreach (var pair in slots.OrderBy(p => p.Key, StringComparer.Ordinal))
                { writer.Write(pair.Key); writer.Write(pair.Value.Schema); writer.Write(pair.Value.Payload.Length); writer.Write(pair.Value.Payload); }
                return Convert.ToBase64String(stream.ToArray());
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Core;
using OniOnline.Network;
using Steamworks;
using UnityEngine;

namespace OniOnline.Runtime
{
    internal sealed class OnlineVoice : MonoBehaviour
    {
        private const int SampleRate = 16000;
        private const int MaxDecodedBytes = SampleRate * 2;
        private const int MaxCaptureBytes = 1024 * 1024;
        private static bool captureNeedsCleanup;
        private sealed class Playback
        {
            internal GameObject Root;
            internal AudioSource Source;
            internal AudioClip Clip;
            internal readonly VoicePcmBuffer Buffer = new VoicePcmBuffer(SampleRate / 2);
            internal double LastFrame;
        }

        private OnlineRuntime runtime;
        private RoomSession attachedRoom;
        private RoomVoiceSession session;
        private readonly Dictionary<string, Playback> playback = new Dictionary<string, Playback>(StringComparer.Ordinal);
        private byte[] compressed = new byte[32 * 1024];
        private byte[] decoded = new byte[20 * 1024];
        private bool held;
        private bool recording;
        private bool draining;
        private bool stopPending;
        private bool captureFaulted;
        private bool discardTail;
        private double nextCapture;
        private double nextStartAttempt;

        internal bool IsTalking { get { return recording; } }
        internal string LastError { get; private set; }

        internal static OnlineVoice Attach(OnlineRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var result = runtime.GetComponent<OnlineVoice>() ?? runtime.gameObject.AddComponent<OnlineVoice>();
            result.runtime = runtime;
            if (captureNeedsCleanup) { result.stopPending = true; result.draining = true; result.discardTail = true; }
            return result;
        }

        internal void SetPushToTalk(bool value)
        {
            held = value;
            if (!value) StopCapture(false);
        }

        private void Update()
        {
            if (runtime == null) return;
            double now = Time.realtimeSinceStartup;
            TryStopCapture();
            if (!ReferenceEquals(attachedRoom, runtime.Session))
            {
                StopCapture(true);
                session?.Dispose(); ClearPlayback();
                attachedRoom = runtime.Session;
                session = attachedRoom == null ? null : new RoomVoiceSession(attachedRoom);
                held = false;
            }
            session?.Pump(now);
            bool connected = runtime.VoiceEnabled && session != null && session.Ready;
            bool allowed = connected && !captureFaulted && OnlineInputControls.ContextAllowsShortcuts &&
                OnlineInputControls.VoiceBindingHeld;
            if (!allowed)
            {
                held = false; StopCapture(true);
            }
            if (!connected) ClearPlayback();
            if (allowed && held && !recording && !draining && !stopPending && now >= nextStartAttempt)
            {
                try
                {
                    if (!SteamRoomCleanup.ClientAvailable)
                    { LastError = "voice_steam_unavailable"; nextStartAttempt = now + 2; }
                    else
                    {
                        captureNeedsCleanup = true;
                        SteamUser.StartVoiceRecording(); recording = true; discardTail = false;
                        LastError = null; nextCapture = now; nextStartAttempt = 0;
                    }
                }
                catch (Exception)
                {
                    LastError = "voice_capture_failed"; nextStartAttempt = now + 2;
                    stopPending = true; draining = true; discardTail = true;
                }
            }
            if ((recording || draining) && !stopPending && !captureFaulted && now >= nextCapture)
            {
                bool current = now - nextCapture <= RoomVoiceSession.QueueLifetimeSeconds;
                nextCapture = now + .1;
                Capture(allowed && !discardTail && current);
            }
            RoomVoiceFrame frame;
            int budget = 8;
            while (session != null && budget-- > 0 && session.TryReceive(out frame))
                if (connected) Decode(frame, now);
            if (playback.Count != 0)
            {
                var players = attachedRoom?.Players;
                foreach (string player in playback.Where(item => now - item.Value.LastFrame > 2 || players == null ||
                    !players.Any(peer => peer.Id == item.Key && peer.Connected)).Select(item => item.Key).ToArray())
                    RemovePlayback(player);
            }
            foreach (var player in playback.Values)
                player.Source.volume = Mathf.Clamp01(runtime.VoiceVolume / 100f);
        }

        private void Capture(bool send)
        {
            try
            {
                if (!SteamRoomCleanup.ClientAvailable) { LastError = "voice_steam_unavailable"; StopCapture(true); return; }
                uint available;
                var result = SteamUser.GetAvailableVoice(out available);
                if (result == EVoiceResult.k_EVoiceResultNotRecording)
                { recording = false; draining = false; captureNeedsCleanup = false; return; }
                if (result == EVoiceResult.k_EVoiceResultNoData) return;
                if (result != EVoiceResult.k_EVoiceResultOK)
                { LastError = "voice_capture_failed"; StopCapture(true); return; }
                if (available == 0) return;
                // A stalled game must discard old captured speech instead of enqueueing
                // seconds of audio. Still drain the SDK so it cannot cross room scopes.
                if (available > MaxCaptureBytes)
                { captureFaulted = true; LastError = "voice_capture_overflow"; StopCapture(true); return; }
                if (available > compressed.Length)
                    compressed = new byte[(int)available];
                uint written;
                result = SteamUser.GetVoice(true, compressed, (uint)compressed.Length, out written);
                if (result == EVoiceResult.k_EVoiceResultNotRecording)
                { recording = false; draining = false; captureNeedsCleanup = false; return; }
                if (result == EVoiceResult.k_EVoiceResultBufferTooSmall)
                {
                    if (written > MaxCaptureBytes) { captureFaulted = true; LastError = "voice_capture_overflow"; }
                    else
                    {
                        LastError = "voice_capture_failed";
                        if (written > compressed.Length) compressed = new byte[(int)written];
                    }
                    StopCapture(true); return;
                }
                if (result == EVoiceResult.k_EVoiceResultNoData) return;
                if (result != EVoiceResult.k_EVoiceResultOK)
                { LastError = "voice_capture_failed"; StopCapture(true); return; }
                if (send && written > 0 && written <= RoomVoiceWire.MaxCompressedBytes)
                    session?.Send(compressed, (int)written);
            }
            catch (Exception) { LastError = "voice_capture_failed"; StopCapture(true); }
        }

        private void StopCapture(bool discard)
        {
            discardTail |= discard;
            if (recording) { recording = false; draining = true; stopPending = true; }
            TryStopCapture();
        }

        private void TryStopCapture()
        {
            if (!stopPending) return;
            try
            {
                if (!SteamRoomCleanup.ClientAvailable)
                { if (!captureFaulted) LastError = "voice_steam_unavailable"; return; }
                SteamUser.StopVoiceRecording(); stopPending = false; draining = true;
            }
            catch (Exception) { discardTail = true; if (!captureFaulted) LastError = "voice_capture_failed"; }
        }

        private void Decode(RoomVoiceFrame frame, double now)
        {
            try
            {
                if (!SteamRoomCleanup.ClientAvailable) { LastError = "voice_steam_unavailable"; return; }
                uint written;
                var result = SteamUser.DecompressVoice(frame.Compressed, (uint)frame.Compressed.Length,
                    decoded, (uint)decoded.Length, out written, SampleRate);
                if (result == EVoiceResult.k_EVoiceResultBufferTooSmall && written <= MaxDecodedBytes)
                {
                    decoded = new byte[Math.Max(decoded.Length, (int)written)];
                    result = SteamUser.DecompressVoice(frame.Compressed, (uint)frame.Compressed.Length,
                        decoded, (uint)decoded.Length, out written, SampleRate);
                }
                if (result != EVoiceResult.k_EVoiceResultOK || written == 0 || written > MaxDecodedBytes || (written & 1) != 0)
                { LastError = "voice_decode_failed"; return; }
                Playback player;
                if (!playback.TryGetValue(frame.PlayerId, out player))
                {
                    if (playback.Count >= RoomVoiceSession.MaxSpeakers) return;
                    player = CreatePlayback(); playback.Add(frame.PlayerId, player);
                }
                player.Buffer.Write(decoded, (int)written); player.LastFrame = now;
                if (!player.Source.isPlaying && player.Buffer.Count >= SampleRate / 10) player.Source.Play();
            }
            catch (Exception) { LastError = "voice_decode_failed"; }
        }

        private Playback CreatePlayback()
        {
            var player = new Playback { Root = new GameObject("OniOnline Voice") };
            try
            {
                player.Root.transform.SetParent(transform, false);
                player.Clip = AudioClip.Create("OniOnline transient voice", SampleRate * 2, 1, SampleRate, true, player.Buffer.Read);
                player.Source = player.Root.AddComponent<AudioSource>();
                player.Source.clip = player.Clip; player.Source.loop = true; player.Source.playOnAwake = false;
                player.Source.spatialBlend = 0; player.Source.volume = Mathf.Clamp01(runtime.VoiceVolume / 100f);
                return player;
            }
            catch
            {
                if (player.Clip != null) Destroy(player.Clip);
                Destroy(player.Root); throw;
            }
        }

        private void RemovePlayback(string playerId)
        {
            Playback player;
            if (!playback.TryGetValue(playerId, out player)) return;
            playback.Remove(playerId); player.Buffer.Clear(); player.Source.Stop();
            Destroy(player.Clip); Destroy(player.Root);
        }

        private void ClearPlayback()
        { foreach (string player in playback.Keys.ToArray()) RemovePlayback(player); }

        private void OnApplicationFocus(bool focused)
        { if (!focused) { held = false; StopCapture(true); } }
        private void OnDisable()
        {
            held = false; StopCapture(true); session?.Dispose(); session = null; attachedRoom = null;
            ClearPlayback();
        }
        private void OnDestroy()
        { StopCapture(true); session?.Dispose(); session = null; ClearPlayback(); }
    }
}

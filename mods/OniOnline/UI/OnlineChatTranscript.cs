using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Network;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // The log owns only presentation. Sequence numbers and delivery remain RoomSession's responsibility.
    internal sealed class OnlineChatTranscript
    {
        private sealed class Entry
        {
            internal long Sequence;
            internal string PlayerId, Name, Text;
            internal RectTransform Rect;
            internal GameObject Badge;
            internal TextMeshProUGUI Number, Sender, Body;
            internal Image Surface;
        }

        private readonly ScrollRect scroll;
        private readonly Transform content;
        private readonly Button latest;
        private readonly int fontSize;
        private readonly List<Entry> entries = new List<Entry>();
        private string roomId;
        private bool layoutPending, followPending, unread;
        private Entry readingAnchor;
        private float readingAnchorY, previousOffset;

        internal OnlineChatTranscript(ScrollRect scroll, Transform content, Button latest, int fontSize)
        {
            this.scroll = scroll; this.content = content; this.latest = latest;
            this.fontSize = fontSize;
            scroll.onValueChanged.AddListener(_ => RefreshLatestButton());
        }

        private bool AtLatest
        {
            get
            {
                float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
                return overflow <= 4 || overflow * Mathf.Clamp01(scroll.verticalNormalizedPosition) <= 12;
            }
        }

        internal void FollowLatest()
        {
            followPending = true; layoutPending = true; unread = false;
            latest.gameObject.SetActive(false);
        }

        private float ViewportY(RectTransform rect, float y)
        {
            return scroll.viewport.InverseTransformPoint(rect.TransformPoint(new Vector3(0, y, 0))).y;
        }

        private void CaptureReadingPosition()
        {
            previousOffset = scroll.content.anchoredPosition.y;
            readingAnchor = null;
            foreach (var entry in entries)
            {
                float top = ViewportY(entry.Rect, entry.Rect.rect.yMax);
                float bottom = ViewportY(entry.Rect, entry.Rect.rect.yMin);
                if (bottom <= scroll.viewport.rect.yMax && top >= scroll.viewport.rect.yMin)
                { readingAnchor = entry; readingAnchorY = top; break; }
            }
        }

        internal void Refresh(string scope, ChatLine[] lines, PlayerInfo[] players)
        {
            bool changedRoom = scope != roomId;
            bool changedLines = changedRoom || entries.Count != lines.Length;
            if (!changedLines)
                for (int i = 0; i < lines.Length; i++)
                    if (!SameLine(entries[i], lines[i])) { changedLines = true; break; }

            if (changedLines)
            {
                bool follow = changedRoom || entries.Count == 0 || AtLatest;
                if (!layoutPending) CaptureReadingPosition();
                // A different room must not inherit an unread indicator or a reading anchor.
                if (changedRoom)
                {
                    foreach (var entry in entries) Remove(entry);
                    entries.Clear(); readingAnchor = null; unread = false;
                }
                // RoomSession only publishes positive, unique sequences. Matching complete lines also
                // keeps presentation safe if local system messages later share a reserved sequence.
                var retained = new List<Entry>(entries);
                var next = new List<Entry>(lines.Length);
                for (int i = 0; i < lines.Length; i++)
                {
                    ChatLine line = lines[i];
                    int found = retained.FindIndex(candidate => SameLine(candidate, line));
                    Entry entry = found >= 0 ? retained[found] : Create(line);
                    if (found >= 0) retained.RemoveAt(found);
                    entry.Rect.SetSiblingIndex(i);
                    entry.Surface.color = (i & 1) == 0 ? OnlineWidgets.InsetColor :
                        new Color(0.205f, 0.215f, 0.26f);
                    next.Add(entry);
                }
                foreach (var entry in retained) Remove(entry);
                entries.Clear(); entries.AddRange(next); roomId = scope;
                followPending = followPending || follow;
                if (!follow) unread = true;
                layoutPending = true;
            }
            foreach (var entry in entries) RefreshSender(entry, players);
            RefreshLatestButton();
        }

        private static bool SameLine(Entry entry, ChatLine line)
        {
            return entry.Sequence == line.Sequence && entry.PlayerId == line.PlayerId &&
                entry.Name == line.Name && entry.Text == line.Text;
        }

        private static void Remove(Entry entry)
        {
            entry.Rect.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(entry.Rect.gameObject);
        }

        private Entry Create(ChatLine line)
        {
            var root = OnlineWidgets.Column(content, "ChatMessage", 10, 4);
            var entry = new Entry { Sequence = line.Sequence, PlayerId = line.PlayerId,
                Name = line.Name, Text = line.Text, Rect = (RectTransform)root };
            entry.Surface = OnlineWidgets.Surface(root.gameObject, OnlineWidgets.InsetColor);
            entry.Surface.raycastTarget = false;
            var heading = OnlineWidgets.Row(root, 26);
            heading.name = "ChatMessageHeading";
            entry.Badge = OnlineWidgets.Rect("ChatPlayerNumber", heading);
            OnlineWidgets.Size(entry.Badge, 28, 26);
            OnlineWidgets.Surface(entry.Badge, OnlineWidgets.PanelColor, true).raycastTarget = false;
            entry.Number = OnlineWidgets.Label(entry.Badge.transform, "", 16);
            entry.Number.alignment = TextAlignmentOptions.Center;
            OnlineWidgets.Stretch(entry.Number.rectTransform);
            entry.Sender = OnlineWidgets.Label(heading, line.Name, 16);
            OnlineWidgets.Size(entry.Sender.gameObject, -1, 26, 1);
            entry.Sender.textWrappingMode = TextWrappingModes.NoWrap;
            entry.Sender.overflowMode = TextOverflowModes.Ellipsis;
            entry.Body = OnlineWidgets.Label(root, line.Text, fontSize);
            entry.Body.name = "ChatMessageBody";
            entry.Body.alignment = TextAlignmentOptions.TopLeft;
            entry.Body.textWrappingMode = TextWrappingModes.Normal;
            entry.Body.overflowMode = TextOverflowModes.Overflow;
            entry.Body.margin = new Vector4(36, 0, 0, 2);
            var bodyLayout = entry.Body.gameObject.AddComponent<LayoutElement>();
            bodyLayout.minWidth = 0; bodyLayout.minHeight = fontSize + 7; bodyLayout.flexibleHeight = 0;
            // Neither sender names nor message bodies ever enable TMP rich text.
            entry.Body.richText = entry.Sender.richText = false;
            return entry;
        }

        private static void RefreshSender(Entry entry, PlayerInfo[] players)
        {
            bool system = string.IsNullOrEmpty(entry.PlayerId);
            var player = players.FirstOrDefault(value => value.Id == entry.PlayerId);
            Color color = player != null ? OnlineWidgets.PlayerColor(player.Number) : OnlineWidgets.MutedColor;
            entry.Badge.SetActive(!system);
            entry.Number.text = player == null ? "·" : player.Number.ToString();
            entry.Number.color = entry.Sender.color = color;
            entry.Sender.text = system ? OnlineText.Get("chat.system") : entry.Name;
            entry.Body.color = system ? OnlineWidgets.MutedColor : OnlineWidgets.TextColor;
            entry.Body.margin = new Vector4(system ? 0 : 36, 0, 0, 2);
        }

        internal void CompleteLayout()
        {
            if (!layoutPending) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
            // A second pass gives wrapped TMP text its final assigned column width.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
            scroll.StopMovement();
            if (followPending)
            {
                scroll.verticalNormalizedPosition = 0; unread = false;
            }
            else
            {
                var position = scroll.content.anchoredPosition;
                if (readingAnchor != null && entries.Contains(readingAnchor))
                    position.y += readingAnchorY - ViewportY(readingAnchor.Rect, readingAnchor.Rect.rect.yMax);
                else position.y = previousOffset;
                position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height));
                scroll.content.anchoredPosition = position;
            }
            layoutPending = followPending = false; readingAnchor = null;
            RefreshLatestButton();
        }

        private void RefreshLatestButton()
        {
            if (!layoutPending && AtLatest) unread = false;
            latest.gameObject.SetActive(unread && !AtLatest);
        }
    }
}

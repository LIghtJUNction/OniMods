using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal sealed class SystemHexMap : MaskableGraphic, IPointerClickHandler, IDragHandler, IScrollHandler
    {
        // UI geometry has a budget, not the number of players or the size of a system.
        private const int GridCellBudget = 1600;
        private StarSystemState state;
        private string signature;
        private readonly Dictionary<string, PlayerInfo> players = new Dictionary<string, PlayerInfo>();
        private readonly Dictionary<int, SystemMember> occupied = new Dictionary<int, SystemMember>();
        private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
        private readonly List<TextMeshProUGUI> planetNames = new List<TextMeshProUGUI>();
        private readonly List<Image> asteroidIcons = new List<Image>();
        private readonly Dictionary<string, Sprite> nativeSprites = new Dictionary<string, Sprite>();
        private Vector2 pan;
        private float zoom = 1.2f;
        private int? selectedQ;
        private int selectedR;
        internal System.Action<int, int> Selected;
        internal int Radius { get { return state?.Radius ?? 12; } }
        private float HexSize { get { return Mathf.Max(0.5f, Mathf.Min(rectTransform.rect.width / (Mathf.Sqrt(3) * (Radius * 2 + 2)),
            rectTransform.rect.height / (Radius * 3 + 3)) * zoom); } }
        private static int Key(int q, int r) { return (q & 0xffff) << 16 | (r & 0xffff); }

        internal void SetState(StarSystemState value, PlayerInfo[] roster, string localPeerId = null)
        {
            roster = roster ?? new PlayerInfo[0];
            string next = (localPeerId ?? "") + ":" + (value?.RoomId ?? "") + ":" + (value?.SystemId ?? "") + ":" + (value?.Revision ?? 0) + ":" +
                string.Join("|", roster.Select(p => p.Id + ":" + p.Number + ":" + p.Name + ":" + p.Connected)) + ":" +
                string.Join("|", (value?.Members ?? new SystemMember[0]).Select(m => m.PeerId + m.Planet?.PlanetId + m.Planet?.Name));
            if (next == signature) return;
            signature = next; state = value;
            players.Clear(); foreach (var player in roster) players[player.Id] = player;
            occupied.Clear();
            if (state != null) foreach (var member in state.Members) occupied[Key(member.Q, member.R)] = member;
            nativeSprites.Clear();
            var own = state?.Members.FirstOrDefault(m => m.PeerId == localPeerId && m.Planet != null);
            var colony = OnlineColonyState.Current;
            if (own != null && colony != null && own.Planet.OwnerId == colony.PersistentOwnerId &&
                own.Planet.HomeSystemId == colony.HomeSystemId && ClusterManager.Instance != null)
            {
                var world = ClusterManager.Instance.GetWorld(own.Planet.NativeWorldId);
                var entity = world == null || !world.IsDiscovered || world.IsModuleInterior ? null : world.GetComponent<ClusterGridEntity>();
                var sprite = entity == null ? null : entity.GetUISprite();
                if (sprite != null) nativeSprites[own.PeerId] = sprite;
            }
            SetVerticesDirty(); DrawLabels();
        }

        internal Vector2 Position(int q, int r)
        { return rectTransform.rect.center + new Vector2(Mathf.Sqrt(3) * (q + r * 0.5f), 1.5f * r) * HexSize + pan; }

        internal void Select(int q, int r)
        { selectedQ = q; selectedR = r; SetVerticesDirty(); DrawLabels(); Selected?.Invoke(q, r); }

        internal void Focus(int q, int r)
        {
            pan -= Position(q, r) - rectTransform.rect.center;
            Select(q, r);
        }

        internal void ResetView()
        { pan = Vector2.zero; zoom = 1.2f; SetVerticesDirty(); DrawLabels(); }

        private bool Visible(Vector2 centre, float margin)
        {
            var bounds = rectTransform.rect;
            return centre.x >= bounds.xMin - margin && centre.x <= bounds.xMax + margin &&
                centre.y >= bounds.yMin - margin && centre.y <= bounds.yMax + margin;
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            DrawStars(vertices);
            float size = HexSize;
            float areaCells = rectTransform.rect.width * rectTransform.rect.height / (2.598f * size * size);
            int stride = Mathf.Max(1, Mathf.CeilToInt(8 / size), Mathf.CeilToInt(Mathf.Sqrt(areaCells / GridCellBudget)));
            int drawn = 0;
            for (int q = -Radius; q <= Radius; q++)
                for (int r = -Radius; r <= Radius; r++)
                {
                    if (Math.Abs(q + r) > Radius || q % stride != 0 || r % stride != 0) continue;
                    Vector2 centre = Position(q, r);
                    if (!Visible(centre, size * stride) || drawn >= GridCellBudget) continue;
                    DrawHex(vertices, centre, size * stride * 0.95f, new Color(0.26f, 0.25f, 0.33f, 0.42f), false);
                    drawn++;
                }
            foreach (var member in occupied.Values)
            {
                Vector2 centre = Position(member.Q, member.R);
                float markerSize = Mathf.Max(size * 0.90f, 10);
                if (!Visible(centre, markerSize)) continue;
                // Dense systems use one small marker per planet rather than unbounded hex meshes.
                if (vertices.currentVertCount > 58000) { Diamond(vertices, centre, 2, OnlineWidgets.MutedColor); continue; }
                PlayerInfo player; players.TryGetValue(member.PeerId, out player);
                Color color = member.Connected ? OnlineWidgets.PlayerColor(player?.Number ?? 0) : OnlineWidgets.MutedColor;
                DrawHex(vertices, centre, markerSize, new Color(color.r * 0.20f, color.g * 0.20f, color.b * 0.20f, 1), true);
                DrawHex(vertices, centre, markerSize, color, false);
                if (member.Planet != null && size >= 15)
                {
                    DrawOrbit(vertices, centre, size * 1.12f, new Color(color.r, color.g, color.b, 0.45f));
                    if (!nativeSprites.ContainsKey(member.PeerId))
                        DrawAsteroid(vertices, centre + Vector2.up * size * 0.22f, size * 0.45f, color);
                }
            }
            if (selectedQ.HasValue)
            {
                Vector2 centre = Position(selectedQ.Value, selectedR);
                if (Visible(centre, size * 1.1f))
                {
                    var chosen = new Color(0.91f, 0.70f, 0.38f);
                    DrawHex(vertices, centre, Mathf.Max(size * 1.12f, 12), chosen, false);
                    if (!occupied.ContainsKey(Key(selectedQ.Value, selectedR)))
                    {
                        DrawHex(vertices, centre, Mathf.Max(size * 0.84f, 8), new Color(chosen.r, chosen.g, chosen.b, 0.13f), true);
                        Diamond(vertices, centre, Mathf.Clamp(size * 0.13f, 2, 6), chosen);
                    }
                }
            }
        }

        private void DrawStars(VertexHelper vertices)
        {
            var bounds = rectTransform.rect;
            // A small static geometry field avoids textures, particles and animation work.
            uint seed = 0x61786f6e;
            for (int i = 0; i < 80; i++)
            {
                seed = seed * 1664525 + 1013904223;
                float x = (seed & 0xffff) / 65535f;
                seed = seed * 1664525 + 1013904223;
                float y = (seed & 0xffff) / 65535f;
                var point = new Vector2(bounds.xMin + x * bounds.width, bounds.yMin + y * bounds.height);
                Diamond(vertices, point, i % 9 == 0 ? 1.2f : 0.65f, new Color(0.51f, 0.49f, 0.61f, i % 9 == 0 ? 0.42f : 0.22f));
            }
        }

        private static void DrawAsteroid(VertexHelper vertices, Vector2 centre, float size, Color color)
        {
            if (vertices.currentVertCount > 63000) return;
            int start = vertices.currentVertCount;
            Color rock = Color.Lerp(color, new Color(0.55f, 0.50f, 0.45f), 0.6f);
            vertices.AddVert(centre, rock, Vector2.zero);
            for (int i = 0; i < 9; i++)
            {
                float angle = i * Mathf.PI * 2 / 9;
                float radius = size * (i % 3 == 0 ? 0.82f : i % 3 == 1 ? 1.02f : 0.92f);
                vertices.AddVert(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, rock, Vector2.zero);
            }
            for (int i = 0; i < 9; i++) vertices.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 9);
            Color crater = Color.Lerp(rock, Color.black, 0.32f);
            Diamond(vertices, centre + new Vector2(-0.26f, 0.18f) * size, size * 0.24f, crater);
            Diamond(vertices, centre + new Vector2(0.29f, -0.22f) * size, size * 0.15f, crater);
        }

        private static void DrawOrbit(VertexHelper vertices, Vector2 centre, float radius, Color color)
        {
            if (vertices.currentVertCount > 63000) return;
            const int segments = 24;
            float width = Mathf.Clamp(radius * 0.018f, 0.6f, 1.4f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                var aDirection = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var bDirection = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                int start = vertices.currentVertCount;
                vertices.AddVert(centre + aDirection * (radius - width), color, Vector2.zero);
                vertices.AddVert(centre + aDirection * (radius + width), color, Vector2.zero);
                vertices.AddVert(centre + bDirection * (radius + width), color, Vector2.zero);
                vertices.AddVert(centre + bDirection * (radius - width), color, Vector2.zero);
                vertices.AddTriangle(start, start + 1, start + 2);
                vertices.AddTriangle(start, start + 2, start + 3);
            }
        }

        private static void Diamond(VertexHelper vertices, Vector2 centre, float size, Color color)
        {
            if (vertices.currentVertCount > 64000) return;
            int start = vertices.currentVertCount;
            vertices.AddVert(centre + Vector2.up * size, color, Vector2.zero);
            vertices.AddVert(centre + Vector2.right * size, color, Vector2.zero);
            vertices.AddVert(centre + Vector2.down * size, color, Vector2.zero);
            vertices.AddVert(centre + Vector2.left * size, color, Vector2.zero);
            vertices.AddTriangle(start, start + 1, start + 2); vertices.AddTriangle(start, start + 2, start + 3);
        }

        private static void DrawHex(VertexHelper vertices, Vector2 centre, float size, Color color, bool fill)
        {
            if (vertices.currentVertCount > 64000) return;
            if (fill)
            {
                int start = vertices.currentVertCount;
                vertices.AddVert(centre, color, Vector2.zero);
                for (int side = 0; side < 6; side++) vertices.AddVert(centre + Corner(side, size), color, Vector2.zero);
                for (int side = 0; side < 6; side++) vertices.AddTriangle(start, start + side + 1, start + (side + 1) % 6 + 1);
                return;
            }
            for (int side = 0; side < 6; side++)
            {
                Vector2 a = centre + Corner(side, size), b = centre + Corner(side + 1, size);
                Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * Mathf.Clamp(size * 0.026f, 0.45f, 1.2f);
                int start = vertices.currentVertCount;
                vertices.AddVert(a + normal, color, Vector2.zero); vertices.AddVert(a - normal, color, Vector2.zero);
                vertices.AddVert(b - normal, color, Vector2.zero); vertices.AddVert(b + normal, color, Vector2.zero);
                vertices.AddTriangle(start, start + 1, start + 2); vertices.AddTriangle(start + 2, start + 3, start);
            }
        }

        private static Vector2 Corner(int side, float size)
        { float angle = (60 * side + 30) * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size; }

        private void DrawLabels()
        {
            int used = 0;
            if (state != null)
                foreach (var member in state.Members)
                {
                    bool selected = selectedQ == member.Q && selectedR == member.R;
                    if (!Visible(Position(member.Q, member.R), HexSize) || (HexSize < 12 && !selected)) continue;
                    TextMeshProUGUI label;
                    if (used == labels.Count)
                    {
                        var icon = OnlineWidgets.Rect("NativeAsteroidIcon", transform).AddComponent<Image>();
                        icon.raycastTarget = false; icon.preserveAspect = true;
                        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                        asteroidIcons.Add(icon);
                        label = OnlineWidgets.Label(transform, "", 16);
                        label.alignment = TextAlignmentOptions.Center;
                        label.textWrappingMode = TextWrappingModes.NoWrap;
                        label.overflowMode = TextOverflowModes.Truncate;
                        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                        labels.Add(label);
                        var name = OnlineWidgets.Label(transform, "", 15);
                        name.gameObject.name = "SystemAsteroidName";
                        name.alignment = TextAlignmentOptions.Center;
                        name.textWrappingMode = TextWrappingModes.NoWrap;
                        name.overflowMode = TextOverflowModes.Ellipsis;
                        name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                        planetNames.Add(name);
                    }
                    label = labels[used++];
                    PlayerInfo player; players.TryGetValue(member.PeerId, out player);
                    label.gameObject.SetActive(true); label.text = (player?.Number ?? 0).ToString();
                    label.fontSize = Mathf.RoundToInt(Mathf.Clamp(HexSize * 0.75f, 13, 24));
                    label.color = member.Connected ? OnlineWidgets.TextColor : OnlineWidgets.MutedColor;
                    label.rectTransform.sizeDelta = new Vector2(Mathf.Max(HexSize * 1.6f, 28), Mathf.Max(HexSize * 1.4f, 26));
                    label.rectTransform.anchoredPosition = Position(member.Q, member.R) - rectTransform.rect.center +
                        (HexSize >= 15 ? Vector2.down * HexSize * 0.56f : Vector2.zero);
                    var planetName = planetNames[used - 1];
                    var asteroidIcon = asteroidIcons[used - 1];
                    Sprite sprite;
                    bool showIcon = nativeSprites.TryGetValue(member.PeerId, out sprite) && HexSize >= 15;
                    asteroidIcon.gameObject.SetActive(showIcon);
                    if (showIcon)
                    {
                        asteroidIcon.sprite = sprite;
                        asteroidIcon.color = Color.white;
                        float iconSize = Mathf.Clamp(HexSize * 1.25f, 22, 120);
                        asteroidIcon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                        asteroidIcon.rectTransform.anchoredPosition = Position(member.Q, member.R) - rectTransform.rect.center + Vector2.up * HexSize * 0.22f;
                    }
                    bool showName = member.Planet != null && (HexSize >= 34 || selected);
                    planetName.gameObject.SetActive(showName);
                    if (showName)
                    {
                        planetName.text = member.Planet.Name;
                        planetName.color = selected ? OnlineWidgets.TextColor : OnlineWidgets.MutedColor;
                        planetName.fontSize = selected ? 17 : 15;
                        planetName.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(HexSize * 3.2f, 120, 230), 28);
                        planetName.rectTransform.anchoredPosition = Position(member.Q, member.R) - rectTransform.rect.center +
                            Vector2.down * (Mathf.Max(HexSize, 15) * 1.36f + 8);
                    }
                }
            for (int i = used; i < labels.Count; i++)
            { labels[i].gameObject.SetActive(false); planetNames[i].gameObject.SetActive(false); asteroidIcons[i].gameObject.SetActive(false); }
        }
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
            Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out point)) return;
            point = (point - rectTransform.rect.center - pan) / HexSize;
            float q = Mathf.Sqrt(3) / 3 * point.x - point.y / 3, r = 2 * point.y / 3;
            int iq = Mathf.RoundToInt(q), ir = Mathf.RoundToInt(r), iy = Mathf.RoundToInt(-q - r);
            float dq = Mathf.Abs(iq - q), dr = Mathf.Abs(ir - r), dy = Mathf.Abs(iy + q + r);
            if (dq > dr && dq > dy) iq = -ir - iy; else if (dr > dy) ir = -iq - iy;
            if (Math.Max(Math.Max(Math.Abs(iq), Math.Abs(ir)), Math.Abs(iq + ir)) <= Radius) Select(iq, ir);
        }
        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right && e.button != PointerEventData.InputButton.Middle) return;
            var canvas = GetComponentInParent<Canvas>();
            pan += e.delta / (canvas == null ? 1 : canvas.scaleFactor); SetVerticesDirty(); DrawLabels();
        }
        public void OnScroll(PointerEventData e)
        {
            Vector2 mouse;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out mouse);
            float previous = zoom;
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1.15f, e.scrollDelta.y), 0.6f, 64);
            pan = mouse - rectTransform.rect.center - (mouse - rectTransform.rect.center - pan) * (zoom / previous);
            SetVerticesDirty(); DrawLabels();
        }
        protected override void OnRectTransformDimensionsChange()
        { base.OnRectTransformDimensionsChange(); if (labels != null) DrawLabels(); }
    }
}

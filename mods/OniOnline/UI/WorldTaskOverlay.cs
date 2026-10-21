using System.Collections.Generic;
using OniOnline.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal enum WorldTaskPhase { Confirmed, Draft, Pending, Rejected, Unconfirmed }
    internal struct WorldTaskMark
    {
        internal WorkCell Cell;
        internal Color Color;
        internal WorldTaskPhase Phase;
    }

    // A visual plan layer over the observed texture. This never predicts terrain mutation.
    internal sealed class WorldTaskOverlay : MaskableGraphic
    {
        private readonly List<WorldTaskMark> marks = new List<WorldTaskMark>();
        private WorldInterestRegion region;
        private WorkCell? selectionStart, selectionEnd;
        private bool validSelection;
        internal int MarkCount { get { return marks.Count; } }
        internal int PendingCount { get { int count = 0; foreach (var mark in marks) if (mark.Phase == WorldTaskPhase.Pending) count++; return count; } }

        internal void Show(WorldInterestRegion viewport, IEnumerable<WorldTaskMark> values,
            WorkCell? start, WorkCell? end, bool valid)
        {
            region = viewport; marks.Clear(); marks.AddRange(values);
            if (marks.Count > 4096)
            {
                // A fully zoomed-out local colony may contain many personal drafts.
                // Coalesce only their visual markers; requests and task cells are untouched.
                int stride = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt((float)region.Width * region.Height / 3600)));
                while (((region.Width + stride - 1) / stride) * ((region.Height + stride - 1) / stride) > 4096) stride++;
                var bins = new Dictionary<int, WorldTaskMark>();
                foreach (var mark in marks)
                {
                    int key = ((mark.Cell.X - region.X) / stride) + ((mark.Cell.Y - region.Y) / stride) * ((region.Width + stride - 1) / stride);
                    WorldTaskMark prior;
                    if (!bins.TryGetValue(key, out prior) || mark.Phase > prior.Phase) bins[key] = mark;
                }
                marks.Clear(); marks.AddRange(bins.Values);
            }
            selectionStart = start; selectionEnd = end; validSelection = valid;
            SetVerticesDirty();
        }
        internal void Clear()
        { marks.Clear(); selectionStart = selectionEnd = null; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (region.Width < 1 || region.Height < 1) return;
            var bounds = rectTransform.rect;
            float width = bounds.width / region.Width, height = bounds.height / region.Height;
            foreach (var mark in marks)
            {
                if (mark.Cell.X < region.X || mark.Cell.Y < region.Y || mark.Cell.X >= region.X + region.Width || mark.Cell.Y >= region.Y + region.Height) continue;
                var cell = new Rect(bounds.xMin + (mark.Cell.X - region.X) * width,
                    bounds.yMin + (mark.Cell.Y - region.Y) * height, width, height);
                cell.xMin += width * .12f; cell.xMax -= width * .12f;
                cell.yMin += height * .12f; cell.yMax -= height * .12f;
                Color color = mark.Color;
                float stroke = Mathf.Clamp(Mathf.Min(width, height) * .08f, .65f, 1.6f);
                if (mark.Phase == WorldTaskPhase.Rejected) color = new Color(.93f, .42f, .38f);
                if (mark.Phase == WorldTaskPhase.Unconfirmed) color = new Color(.94f, .73f, .36f);
                if (mark.Phase == WorldTaskPhase.Pending || mark.Phase == WorldTaskPhase.Unconfirmed)
                    Outline(mesh, cell, stroke, color);
                else
                {
                    Line(mesh, new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMax), stroke, color);
                    if (mark.Phase != WorldTaskPhase.Draft)
                        Line(mesh, new Vector2(cell.xMin, cell.yMax), new Vector2(cell.xMax, cell.yMin), stroke, color);
                }
                if (mark.Phase == WorldTaskPhase.Unconfirmed)
                    Line(mesh, new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMax), stroke, color);
            }
            if (!selectionStart.HasValue || !selectionEnd.HasValue) return;
            var a = selectionStart.Value; var b = selectionEnd.Value;
            int left = Mathf.Max(region.X, Mathf.Min(a.X, b.X)), right = Mathf.Min(region.X + region.Width, Mathf.Max(a.X, b.X) + 1);
            int bottom = Mathf.Max(region.Y, Mathf.Min(a.Y, b.Y)), top = Mathf.Min(region.Y + region.Height, Mathf.Max(a.Y, b.Y) + 1);
            if (left >= right || bottom >= top) return;
            var selected = new Rect(bounds.xMin + (left - region.X) * width, bounds.yMin + (bottom - region.Y) * height,
                (right - left) * width, (top - bottom) * height);
            Outline(mesh, selected, 1.5f, validSelection ? new Color(.97f, .79f, .42f) : new Color(.93f, .42f, .38f));
        }
        private static void Outline(VertexHelper mesh, Rect rect, float thickness, Color color)
        {
            int start = mesh.currentVertCount;
            var min = rect.min; var max = rect.max;
            float inset = Mathf.Min(thickness, Mathf.Min(rect.width, rect.height) * .3f);
            mesh.AddVert(new Vector2(min.x, min.y), color, Vector2.zero);
            mesh.AddVert(new Vector2(max.x, min.y), color, Vector2.zero);
            mesh.AddVert(new Vector2(max.x, max.y), color, Vector2.zero);
            mesh.AddVert(new Vector2(min.x, max.y), color, Vector2.zero);
            mesh.AddVert(new Vector2(min.x + inset, min.y + inset), color, Vector2.zero);
            mesh.AddVert(new Vector2(max.x - inset, min.y + inset), color, Vector2.zero);
            mesh.AddVert(new Vector2(max.x - inset, max.y - inset), color, Vector2.zero);
            mesh.AddVert(new Vector2(min.x + inset, max.y - inset), color, Vector2.zero);
            for (int side = 0; side < 4; side++)
            {
                int next = (side + 1) % 4;
                mesh.AddTriangle(start + side, start + next, start + 4 + next);
                mesh.AddTriangle(start + side, start + 4 + next, start + 4 + side);
            }
        }
        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float thickness, Color color)
        {
            var delta = b - a;
            var normal = new Vector2(-delta.y, delta.x).normalized * thickness * .5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a + normal, color, Vector2.zero); mesh.AddVert(a - normal, color, Vector2.zero);
            mesh.AddVert(b - normal, color, Vector2.zero); mesh.AddVert(b + normal, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start + 2, start + 3, start);
        }
    }
}

using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class CurrentStateReadTools
    {
        private static JArray ViewFiles()
        {
            return new JArray
            {
                FileHint("/active/map/viewport.md", "Editable map current camera viewport; move camera change this visible range."),
                FileHint("/active/map/cell_X_Y.md", "Exact cell detail: element, objects, footprint/pivot, ports, line links, power role, pickup stacks, decision hints, quick ops."),
                FileHint("/active/map/zoom_X1_Y1_X2_Y2.md", "Local multi-view zoom; pass views=default,power,temperature compact=true to sync camera and inspect details."),
                FileHint("/active/screenshots/index.md", "Viewport screenshots; use captureVisible=true views=default,power,temperature waitFrames=2 for stream verification."),
                FileHint("/active/dupes/reachability.md", "Optional duplicant movement range: compact reachable cells before rescue, dig, build, or access fixes."),
                FileHint("/active/infrastructure/power.md", "Low-token power audit: per-cell glyph/dirs/links/to, bridges, circuits, producers, consumers, batteries."),
                FileHint("/active/infrastructure/liquid_conduits.md", "Low-token liquid audit: pipe glyph/dirs/links/to, bridges, input ports, output ports."),
                FileHint("/active/infrastructure/gas_conduits.md", "Low-token gas audit: pipe glyph/dirs/links/to, bridges, input ports, output ports."),
                FileHint("/active/infrastructure/logic.md", "Low-token automation audit: wire glyph/dirs/links/to, bridges, signal input/output ports."),
                FileHint("/active/infrastructure/solid_conveyor.md", "Low-token rail audit: rail glyph/dirs/links/to, bridges, loader inputs, receptacle outputs.")
            };
        }

        private static JObject LiveViewport()
        {
            var camera = Camera.main;
            if (camera == null)
                return new JObject { ["ok"] = false, ["reason"] = "Camera not initialized" };

            float size = camera.orthographicSize;
            float aspect = camera.aspect;
            int x1 = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.x - size * aspect), 0, Grid.WidthInCells - 1);
            int x2 = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.x + size * aspect), 0, Grid.WidthInCells - 1);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.y - size), 0, Grid.HeightInCells - 1);
            int y2 = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.y + size), 0, Grid.HeightInCells - 1);

            return new JObject
            {
                ["ok"] = true,
                ["center"] = new JObject
                {
                    ["x"] = Mathf.RoundToInt(camera.transform.position.x),
                    ["y"] = Mathf.RoundToInt(camera.transform.position.y)
                },
                ["bounds"] = new JObject { ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 },
                ["readVisible"] = "world_editor command=read path=/active/map/viewport.md compact=true view=default",
                ["readPowerVisible"] = "world_editor command=read path=/active/infrastructure/power.md compact=true syncView=true",
                ["captureVisible"] = "world_editor command=screenshot views=default,power,temperature waitFrames=2",
                ["zoomHere"] = "world_editor command=zoom x1=" + x1 + " y1=" + y1 + " x2=" + x2 + " y2=" + y2 + " views=default,power,temperature compact=true"
            };
        }

        private static JArray LookAroundPlan()
        {
            var camera = Camera.main;
            if (camera == null)
                return new JArray();

            int halfWidth = 10;
            int halfHeight = 7;
            int cx = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.x), 0, Grid.WidthInCells - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.y), 0, Grid.HeightInCells - 1);
            int stepX = halfWidth + 1;
            int stepY = halfHeight + 1;

            return new JArray
            {
                LookAroundStep("center_detail", cx, cy, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("overview", cx, cy, halfWidth * 2, halfHeight * 2, "overview", "default,oxygen,temperature"),
                LookAroundStep("north", cx, cy + stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("south", cx, cy - stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("east", cx + stepX, cy, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("west", cx - stepX, cy, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("north_east", cx + stepX, cy + stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("north_west", cx - stepX, cy + stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("south_east", cx + stepX, cy - stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("south_west", cx - stepX, cy - stepY, halfWidth, halfHeight, "detail", "default,power,temperature")
            };
        }

        private static JObject LookAroundStep(string direction, int cx, int cy, int halfWidth, int halfHeight, string focusMode, string views)
        {
            int x1 = Mathf.Clamp(cx - halfWidth, 0, Grid.WidthInCells - 1);
            int x2 = Mathf.Clamp(cx + halfWidth, 0, Grid.WidthInCells - 1);
            int y1 = Mathf.Clamp(cy - halfHeight, 0, Grid.HeightInCells - 1);
            int y2 = Mathf.Clamp(cy + halfHeight, 0, Grid.HeightInCells - 1);
            var args = new JObject { ["command"] = "zoom", ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2, ["views"] = views, ["compact"] = true, ["syncView"] = true, ["focusCamera"] = true, ["focusMode"] = focusMode };
            string call = "world_editor command=zoom x1=" + x1 + " y1=" + y1 + " x2=" + x2 + " y2=" + y2
                + " views=" + views + " compact=true syncView=true focusMode=" + focusMode;
            string why = focusMode == "overview" ? "Zoom out to anchor global layout before planning edits." : "Zoom in to inspect local cells, overlays, anchors, and stream-visible detail.";
            return new JObject { ["direction"] = direction, ["tool"] = "world_editor", ["arguments"] = args, ["call"] = call, ["why"] = why };
        }

        private static JArray ProgressiveDetail()
        {
            return new JArray
            {
                DetailHint("overview", "read_control domain=state action=current", "Small first call: colony snapshot, editable files, next actions."),
                DetailHint("logs", "world_editor command=read path=/active/diagnostics/logs.md logLimit=220", "Low-token Player.log stability check after crashes, tester failures, mod exceptions."),
                DetailHint("zoom", "world_editor command=zoom x1=... y1=... x2=... y2=... views=default,power,temperature", "Local multi-view map; syncs live camera/view for stream."),
                DetailHint("screenshot", "world_editor command=screenshot views=default,power,temperature waitFrames=2", "Capture current viewport across overlays; use as visual proof after map/connection edits."),
                DetailHint("cell", "/active/map/cell_X_Y.md", "Exact cell: temperature suitability, objects, ports, lines, pickup stacks, Decision Hints for dig/mop/sweep/network risks."),
                DetailHint("ports", "read_control domain=infrastructure action=nearby_ports x=... y=... radius=8 kind=all", "Local power/liquid/gas/logic/rail ports without broad scans."),
                DetailHint("reachability", "read_control domain=state action=current includeReachability=true reachabilityRadius=12", "Compact duplicant movement range before rescue, dig, construction planning; use standalone reachable_area only for repeated checks."),
                DetailHint("ops", "world_editor command=read path=/active/ops/tools.md", "Grep-friendly operation file/tool index before issuing natural orders."),
                DetailHint("edit", "/active/ops/orders.md, /active/ops/dupes.md, or /active/map/viewport.md SEARCH/REPLACE", "Execute typed orders, duplicant moves, or map-token edits after inspecting local detail.")
            };
        }

        private static JObject FileHint(string path, string purpose)
        {
            return new JObject { ["path"] = path, ["purpose"] = purpose };
        }

        private static JObject DetailHint(string step, string call, string purpose)
        {
            return new JObject { ["step"] = step, ["call"] = call, ["purpose"] = purpose };
        }
    }
}

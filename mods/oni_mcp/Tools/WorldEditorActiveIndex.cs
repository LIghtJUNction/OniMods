using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private const int ActiveIndexExpandedStateLimit = 9000;

        static string ReadActiveIndexMarkdown(JObject args)
        {
            args = args ?? new JObject();

            var sb = new StringBuilder();
            int activeWorldId = ClusterManager.Instance?.activeWorldId ?? -1;
            var activeWorld = activeWorldId >= 0 ? ClusterManager.Instance?.GetWorld(activeWorldId) : null;
            float cyclePercent = GameClock.Instance?.GetCurrentCycleAsPercentage() ?? 0f;
            bool paused = SpeedControlScreen.Instance != null
                ? SpeedControlScreen.Instance.IsPaused
                : Time.timeScale == 0f;
            int speed = SpeedControlScreen.Instance != null ? SpeedControlScreen.Instance.GetSpeed() + 1 : 0;
            int dupes = Components.LiveMinionIdentities?.Items?.Count ?? 0;

            sb.AppendLine("# Active World");
            sb.AppendLine();
            sb.AppendLine("- Real Time: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            sb.AppendLine("- Save: " + ActiveSaveDisplayName());
            sb.AppendLine("- Game: Cycle " + GameUtil.GetCurrentCycle() + ", " + Math.Round(cyclePercent * 100f, 1) + "%");
            sb.AppendLine("- State: " + (paused ? "paused" : "running") + ", speed=" + speed + ", timeScale=" + Time.timeScale.ToString("F2"));
            sb.AppendLine("- Active World: " + activeWorldId + (activeWorld == null ? string.Empty : " (" + ToolUtil.CleanName(activeWorld.GetProperName()) + ")"));
            sb.AppendLine("- Duplicants: " + dupes);
            sb.AppendLine();

            AppendProgressiveOptions(sb);
            AppendNextCalls(sb);
            AppendEditableFiles(sb);

            if (ShouldIncludeExpandedState(args))
                AppendExpandedCurrentState(sb, args);

            return sb.ToString();
        }

        private static void AppendProgressiveOptions(StringBuilder sb)
        {
            sb.AppendLine("## Progressive Options");
            sb.AppendLine("- Default: low-token status and next calls only.");
            sb.AppendLine("- `includeState=true`: append current colony JSON snapshot.");
            sb.AppendLine("- `includeInfrastructure=true infrastructureKind=all|power|liquid|gas|logic|rail`: append compact ports/lines.");
            sb.AppendLine("- Infrastructure files expose low-token `glyph/dirs/links/to`, bridges, ports, and producer/consumer roles.");
            sb.AppendLine("- `includeLogs=true logLimit=160`: append recent suspicious Player.log lines.");
            sb.AppendLine("- `/active/diagnostics/logs.md`: focused log stability audit without large world state.");
            sb.AppendLine("- `detail=full`: equivalent to `includeState=true`.");
            sb.AppendLine();
        }

        private static void AppendNextCalls(StringBuilder sb)
        {
            sb.AppendLine("## Next Calls");
            sb.AppendLine("- Starter response includes `executionPlan`: interior digs, shell/divider/doors, Outhouse, WashBasin, ResearchCenter, and sweep follow-up.");
            sb.AppendLine("- First call: `world_editor command=read path=/active/index.md includeState=true` returns compact world state.");
            sb.AppendLine("- Second call starter: `building_control domain=planning action=room_template kind=starter autoLayout=true priority=7 execute=true confirm=true` builds toilet, wash basin, research station, shell, doors, and interior digs.");
            sb.AppendLine("- Viewport map: `world_editor command=read path=/active/map/viewport.md format=edit compact=false view=default`; move the camera to change this visible range.");
            sb.AppendLine("- Switch live view: pass `view=power|liquid|gas|logic|solid|temperature|decor|germs|farming|rooms|materials` on map reads.");
            sb.AppendLine("- Multi-view zoom: `world_editor command=zoom x1=80 y1=140 x2=105 y2=155 views=default,power,oxygen,temperature`");
            sb.AppendLine("- Look around: first current-state response includes `lookAroundPlan` center/cardinal/diagonal/overview zoom calls.");
            sb.AppendLine("- Cell details: `world_editor command=read path=/active/map/cell_X_Y.md` includes element, building, dupe/critter, pivot, footprint, ports, lines, dropped pickups, and Decision Hints for dig/mop/sweep/network risks.");
            sb.AppendLine("- Dupe reachability: `world_editor command=read path=/active/dupes/reachability.md radius=12 sampleLimit=12` before rescue/dig/build plans.");
            sb.AppendLine("- Stability logs: `world_editor command=read path=/active/diagnostics/logs.md logLimit=220` after crashes or tester failures.");
            sb.AppendLine("- Orders file: `world_editor command=read path=/active/ops/orders.md` supports 挖/擦/扫/毒/拆/杀/收/消/捕 plus `:priority`.");
            sb.AppendLine("- Dupe ops file: `world_editor command=read path=/active/ops/dupes.md` supports 移/移动 for duplicants.");
            sb.AppendLine("- Operation syntax: `world_editor command=read path=/active/ops/tools.md` lists typed files and grep-friendly tools before editing.");
            sb.AppendLine("- Fast edit loop: read index -> starter room_template -> cell detail or ops/tools only if the result asks for it.");
            sb.AppendLine();
        }

        private static void AppendEditableFiles(StringBuilder sb)
        {
            sb.AppendLine("## Editable Files");
            sb.AppendLine("- `/active/dupes/index.md`");
            sb.AppendLine("- `/active/dupes/reachability.md`");
            sb.AppendLine("- `/active/management/index.md`");
            sb.AppendLine("- `/active/management/schedule.md`");
            sb.AppendLine("- `/active/management/priorities.md`");
            sb.AppendLine("- `/active/management/food.md`");
            sb.AppendLine("- `/active/management/skills.md`");
            sb.AppendLine("- `/active/management/research.md`");
            sb.AppendLine("- `/active/diagnostics/logs.md`");
            sb.AppendLine("- `/active/ops/tools.md`");
            sb.AppendLine("- `/active/ops/orders.md`");
            sb.AppendLine("- `/active/ops/dupes.md`");
            sb.AppendLine("- `/active/ops/any.md`");
            sb.AppendLine("- `/active/map/viewport.md`");
            sb.AppendLine("- `/active/infrastructure/power.md`");
            sb.AppendLine("- `/active/infrastructure/liquid_conduits.md`");
            sb.AppendLine("- `/active/infrastructure/gas_conduits.md`");
            sb.AppendLine("- `/active/infrastructure/logic.md`");
            sb.AppendLine("- `/active/infrastructure/solid_conveyor.md`");
            sb.AppendLine();
            sb.AppendLine("## Quick Edits");
            sb.AppendLine("- Rename dupe: read `/active/dupes/index.md`, open listed detail file, replace `Name:`.");
            sb.AppendLine("- Schedule block: edit `/active/management/schedule.md` with `set_block schedule=\"AI轮班-1\" hour=7 group=Worktime`.");
            sb.AppendLine("- Assign schedule: edit `/active/management/schedule.md` with `assign_dupe name=\"Dig\" schedule=\"AI轮班-1\"`.");
            sb.AppendLine("- Priority: edit `/active/management/priorities.md` with `priority name=\"Dig\" category=\"Dig\" value=7`.");
            sb.AppendLine("- Orders: edit `/active/ops/orders.md` with `挖/擦/扫/毒/拆/杀/收/消/捕 ... :priority dryRun=true`.");
            sb.AppendLine("- Move: edit `/active/ops/dupes.md` with `移 人@Dig -> 研究站 confirm=true`; critters use `捕`, items use `扫` plus storage.");
            sb.AppendLine();
        }

        private static bool ShouldIncludeExpandedState(JObject args)
        {
            if (ToolUtil.GetBool(args, "includeState", false))
                return true;

            string detail = FirstZoomText(args, "detail", "profile", "format");
            return detail.Equals("full", StringComparison.OrdinalIgnoreCase)
                || detail.Equals("state", StringComparison.OrdinalIgnoreCase)
                || detail.Equals("current", StringComparison.OrdinalIgnoreCase);
        }

        private static void AppendExpandedCurrentState(StringBuilder sb, JObject args)
        {
            var forwarded = new JObject(args);
            forwarded["domain"] = "state";
            forwarded["action"] = "current";
            forwarded.Remove("command");
            forwarded.Remove("path");

            var result = CurrentStateReadTools.ReadCurrent(forwarded);
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;

            sb.AppendLine("## Expanded Current State");
            if (result.IsError)
            {
                sb.AppendLine("State read failed:");
                sb.AppendLine("```text");
                sb.AppendLine(TrimActiveIndexText(text, ActiveIndexExpandedStateLimit));
                sb.AppendLine("```");
                return;
            }

            sb.AppendLine("```json");
            sb.AppendLine(TrimActiveIndexText(text, ActiveIndexExpandedStateLimit));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        private static string TrimActiveIndexText(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;

            return text.Substring(0, max) + "\n... truncated; call read_control domain=state action=current for full output";
        }

        private static string ActiveSaveDisplayName()
        {
            string path = SaveLoader.GetActiveSaveFilePath();
            if (!string.IsNullOrEmpty(path))
                return Path.GetFileNameWithoutExtension(path);

            try
            {
                if (SaveLoader.Instance != null && !string.IsNullOrEmpty(SaveLoader.Instance.GameInfo.baseName))
                    return SaveLoader.Instance.GameInfo.baseName;
            }
            catch
            {
            }

            return "unknown";
        }
    }
}

using System;
using System.Linq;
using System.Collections.Generic;
using OniOnline.Multiplayer;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // Personal predictions and canonical plans are deliberately different views.
    internal sealed class WorkPlanWindow : KScreen
    {
        private OnlineRuntime runtime;
        private string ownerId;
        private string planetId;
        private string actorId;
        private bool merged;
        private int page;
        private GameObject root;
        private Transform rows;
        private TextMeshProUGUI modeLabel;
        private TextMeshProUGUI stateLabel;
        private TextMeshProUGUI pageLabel;
        private Button mineTab, memberTab, mergedTab, previousPage, nextPage;
        private Button allowMap, allowPlans, allowTasks;
        private sealed class PlanLine
        {
            internal string Text;
            internal WorkTaskIntent Plan;
        }
        private sealed class PlanRow
        {
            internal GameObject Root;
            internal TextMeshProUGUI Label;
            internal Button Cancel;
            internal WorkTaskIntent Plan;
        }
        private readonly List<PlanRow> planRows = new List<PlanRow>();
        private readonly Dictionary<string, string> cancellations = new Dictionary<string, string>();
        private long revision = -1;
        private double nextPoll;
        private const int PageSize = 32;
        private static string T(string key) { return OnlineText.Get(key); }

        internal static WorkPlanWindow Create(OnlineRuntime runtime, string owner, string planet)
        {
            var root = OnlineWidgets.CanvasRoot("OniOnlineWorkPlans", 30025);
            var shade = OnlineWidgets.Rect("Shade", root.transform);
            OnlineWidgets.Stretch((RectTransform)shade.transform);
            OnlineWidgets.Surface(shade, new Color(0, 0, 0, 0.65f));
            var panel = OnlineWidgets.Rect("WorkPlans", root.transform);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.08f, 0.10f); rect.anchorMax = new Vector2(0.92f, 0.90f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            OnlineWidgets.Surface(panel, OnlineWidgets.PanelColor, true);
            var view = panel.AddComponent<WorkPlanWindow>();
            view.root = root; view.runtime = runtime; view.ownerId = owner; view.planetId = planet;
            view.actorId = runtime.Session.LocalPlayerId;
            view.Build(); view.Activate(); return view;
        }

        private void Build()
        {
            var body = OnlineWidgets.Column(transform, "PlansBody", 14, 10);
            OnlineWidgets.Stretch((RectTransform)body);
            var header = OnlineWidgets.Row(body, 40);
            OnlineWidgets.Surface(header.gameObject, OnlineWidgets.AccentColor, true);
            header.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 4, 3, 3);
            var title = OnlineWidgets.Label(header, T("work.title"), 21);
            OnlineWidgets.Size(title.gameObject, -1, 40, 1);
            OnlineWidgets.Button(header, T("moderation.close"), Close, 90);
            var tabs = OnlineWidgets.Row(body, 40);
            mineTab = OnlineWidgets.Button(tabs, T("work.mine"), () => Select(false, runtime.Session.LocalPlayerId), -1);
            memberTab = OnlineWidgets.Button(tabs, T("work.member"), NextActor, -1);
            mergedTab = OnlineWidgets.Button(tabs, T("work.merged"), () => Select(true, null), -1);
            var permissions = OnlineWidgets.Row(body, 38);
            allowMap = OnlineWidgets.Button(permissions, "", () => TogglePermission(RemoteWorkPermission.ViewMap), -1);
            allowPlans = OnlineWidgets.Button(permissions, "", () => TogglePermission(RemoteWorkPermission.ViewPlans), -1);
            allowTasks = OnlineWidgets.Button(permissions, "", () => TogglePermission(RemoteWorkPermission.IssueTasks), -1);
            modeLabel = OnlineWidgets.Label(body, "", 18); OnlineWidgets.Size(modeLabel.gameObject, -1, 28);
            stateLabel = OnlineWidgets.Label(body, T("work.hint"), 16);
            stateLabel.textWrappingMode = TextWrappingModes.Normal;
            stateLabel.color = OnlineWidgets.MutedColor;
            OnlineWidgets.Size(stateLabel.gameObject, -1, 50);
            var scroll = OnlineWidgets.Scroll(body, 300, out rows);
            var size = scroll.GetComponent<UnityEngine.UI.LayoutElement>(); size.flexibleHeight = 1;
            var pages = OnlineWidgets.Row(body, 38);
            previousPage = OnlineWidgets.Button(pages, T("art.previous_page"), () => { page = Math.Max(0, page - 1); Refresh(); }, 140);
            pageLabel = OnlineWidgets.Label(pages, "", 16);
            pageLabel.alignment = TextAlignmentOptions.Center;
            OnlineWidgets.Size(pageLabel.gameObject, -1, 38, 1);
            nextPage = OnlineWidgets.Button(pages, T("art.next_page"), () => { page++; Refresh(); }, 140);
            Refresh();
        }

        private void Select(bool isMerged, string actor)
        { merged = isMerged; actorId = actor; page = 0; nextPoll = 0; Refresh(); }
        private void NextActor()
        {
            var players = runtime.Session?.Players;
            if (players == null || players.Length == 0) return;
            int index = Array.FindIndex(players, p => p.Id == actorId);
            Select(false, players[(index + 1) % players.Length].Id);
        }
        private void Update()
        {
            if (runtime.Session == null || runtime.Multiplayer == null) { Close(); return; }
            if (Time.realtimeSinceStartupAsDouble >= nextPoll)
            {
                nextPoll = Time.realtimeSinceStartupAsDouble + 3;
                runtime.Multiplayer.Work.RequestPlans(ownerId, planetId, merged, actorId);
            }
            if (revision != runtime.Revision) Refresh();
        }
        private void Refresh()
        {
            if (runtime.Multiplayer == null || runtime.Session == null) return;
            revision = runtime.Revision;
            var work = runtime.Multiplayer.Work;
            string actorName = runtime.Session.Players.FirstOrDefault(p => p.Id == actorId)?.Name ?? T("work.mine");
            modeLabel.text = merged ? T("work.merged") : T("work.personal") + " · " + actorName;
            stateLabel.text = T("work.hint");
            if (merged && work.ExecutionError(ownerId, planetId) != null)
                stateLabel.text = T("work.result_uncertain");
            OnlineWidgets.Tint(mineTab, !merged && actorId == runtime.Session.LocalPlayerId ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            OnlineWidgets.Tint(memberTab, !merged && actorId != runtime.Session.LocalPlayerId ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            OnlineWidgets.Tint(mergedTab, merged ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            bool canGrant = !merged && ownerId == runtime.Session.LocalPlayerId && actorId != null && actorId != ownerId;
            var direct = canGrant ? runtime.Multiplayer.GetDirectWorkPermissions(actorId) : RemoteWorkPermission.None;
            PermissionButton(allowMap, "work.allow_map", RemoteWorkPermission.ViewMap, direct, canGrant);
            PermissionButton(allowPlans, "work.allow_plans", RemoteWorkPermission.ViewPlans, direct, canGrant);
            PermissionButton(allowTasks, "work.allow_tasks", RemoteWorkPermission.IssueTasks, direct, canGrant);
            var plans = work.GetPlans(ownerId, planetId, merged, actorId);
            if (!merged && actorId == runtime.Session.LocalPlayerId)
            {
                var currentKeys = new HashSet<string>(plans.Select(PlanKey));
                foreach (string key in cancellations.Keys.Where(key => !currentKeys.Contains(key)).ToArray()) cancellations.Remove(key);
            }
            var lines = plans.OrderByDescending(p => p.Priority).ThenBy(p => p.Cell.Y).ThenBy(p => p.Cell.X)
                .Select(p => new PlanLine { Plan = p, Text = Format(p.ActorId, p.Cell, p.Priority,
                    CancellationStatus(p) ?? T(p.Build != null && p.Historical && !p.HistoricalActive ? "work.unconfirmed" : "work.confirmed"), p.Build) }).ToList();
            if (!merged && actorId == runtime.Session.LocalPlayerId)
                foreach (var draft in work.OwnDrafts(planetId))
                {
                    var status = work.GetSubmission(draft.RequestId);
                    if (status != null && status.Accepted && !status.DryRun) continue;
                    foreach (var cell in draft.Cells)
                        lines.Add(new PlanLine { Text = Format(actorId, cell, draft.Priority, Status(status), draft.Build) });
                }
            int pageCount = Math.Max(1, (lines.Count + PageSize - 1) / PageSize);
            page = Math.Min(page, pageCount - 1);
            pageLabel.text = (page + 1) + " / " + pageCount;
            previousPage.interactable = page > 0; nextPage.interactable = page + 1 < pageCount;
            if (lines.Count == 0) lines.Add(new PlanLine { Text = T("work.empty") });
            int index = 0;
            foreach (var line in lines.Skip(page * PageSize).Take(PageSize))
            {
                if (index == planRows.Count)
                {
                    var row = OnlineWidgets.Row(rows, 54);
                    OnlineWidgets.Surface(row.gameObject, new Color(0.24f, 0.255f, 0.31f));
                    row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(10, 6, 5, 5);
                    var label = OnlineWidgets.Label(row, "", 17);
                    OnlineWidgets.Size(label.gameObject, -1, 44, 1);
                    label.textWrappingMode = TextWrappingModes.Normal;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                    var item = new PlanRow { Root = row.gameObject, Label = label };
                    item.Cancel = OnlineWidgets.Button(row, T("work.cancel_plan"), () => CancelPlan(item.Plan), 120);
                    planRows.Add(item);
                }
                var view = planRows[index++];
                view.Root.SetActive(true); view.Label.text = line.Text; view.Plan = line.Plan;
                bool ownPlan = !merged && actorId == runtime.Session.LocalPlayerId && line.Plan != null && line.Plan.ActorId == actorId;
                view.Cancel.gameObject.SetActive(ownPlan);
                if (ownPlan)
                {
                    var grant = work.GetReceivedGrant(ownerId, planetId);
                    view.Cancel.interactable = runtime.Session.Joined && !runtime.Session.Reconnecting && !runtime.Session.Leaving &&
                        runtime.Session.Players.Any(p => p.Id == ownerId && p.Connected) &&
                        work.ExecutionError(ownerId, planetId) == null && !CancellationPending(line.Plan) &&
                        (ownerId == actorId || grant != null && (grant.Permissions & RemoteWorkPermission.IssueTasks) != 0);
                }
            }
            for (int i = index; i < planRows.Count; i++) planRows[i].Root.SetActive(false);
        }

        private static string PlanKey(WorkTaskIntent plan)
        { return plan.RequestId + ":" + plan.ConfirmedRevision + ":" + plan.Key; }

        private RemoteWorkReceipt CancellationReceipt(WorkTaskIntent plan)
        {
            string request;
            return cancellations.TryGetValue(PlanKey(plan), out request) ? runtime.Multiplayer.Work.GetSubmission(request) : null;
        }

        private bool CancellationPending(WorkTaskIntent plan)
        {
            var receipt = CancellationReceipt(plan);
            return receipt != null && (receipt.Reason == "work_queued" || receipt.Reason == "work_cancelled" ||
                receipt.Reason == "work_execution_uncertain");
        }

        private string CancellationStatus(WorkTaskIntent plan)
        {
            var receipt = CancellationReceipt(plan);
            if (receipt == null) return null;
            if (receipt.Accepted) return T("work.cancelled");
            if (receipt.Reason == "work_queued") return T("work.cancelling");
            if (receipt.Reason == "work_response_unconfirmed" || receipt.Reason == "work_execution_uncertain") return T("work.unconfirmed");
            return T("work.cancel_rejected");
        }

        private void CancelPlan(WorkTaskIntent plan)
        {
            if (plan == null || runtime.Session == null || runtime.Multiplayer == null || merged ||
                plan.ActorId != runtime.Session.LocalPlayerId || CancellationPending(plan)) return;
            var work = runtime.Multiplayer.Work;
            if (!work.CancelPlan(ownerId, plan)) { stateLabel.text = T("work.unavailable"); return; }
            // Local-owner receipts can arrive synchronously. Query the stored receipt
            // after submission; retain the plan until the authority refreshes it.
            cancellations[PlanKey(plan)] = work.LastSubmittedRequestId;
            Refresh();
        }
        private void TogglePermission(RemoteWorkPermission flag)
        {
            if (ownerId != runtime.Session?.LocalPlayerId || actorId == null || actorId == ownerId || merged) return;
            try
            { runtime.Multiplayer.SetWorkPermissions(actorId, runtime.Multiplayer.GetDirectWorkPermissions(actorId) ^ flag); }
            catch (InvalidOperationException) { stateLabel.text = T("work.unavailable"); }
            Refresh();
        }
        private static void PermissionButton(Button button, string key, RemoteWorkPermission flag,
            RemoteWorkPermission permissions, bool enabled)
        {
            bool granted = (permissions & flag) != 0;
            button.GetComponentInChildren<TextMeshProUGUI>().text = T(key) + " · " + T(granted ? "ui.enabled" : "ui.disabled");
            button.interactable = enabled;
            OnlineWidgets.Tint(button, granted ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
        }
        private string Format(string actor, WorkCell cell, int priority, string status, WorkBuildSpec build = null)
        {
            var player = runtime.Session.Players.FirstOrDefault(p => p.Id == actor);
            string task = T("work.dig");
            if (build != null)
            {
                var def = Assets.GetBuildingDef(build.PrefabId);
                task = def == null ? build.PrefabId : global::STRINGS.UI.StripLinkFormatting(def.Name);
                task += build.PrefabId == "Outhouse" ? " 2×3" : " 1×2";
            }
            return (player?.Number.ToString() ?? "?") + " " + (player?.Name ?? actor) + " · " + task
                + " (" + cell.X + ", " + cell.Y + ") · P" + priority + " · " + status;
        }
        private static string Status(RemoteWorkReceipt receipt)
        {
            if (receipt == null || receipt.Reason == "work_draft") return T("work.draft");
            if (receipt.Reason == "work_queued" || receipt.Reason == "work_confirming") return T("work.pending");
            if (receipt.Reason == "work_response_unconfirmed" || receipt.Reason == "work_execution_uncertain") return T("work.unconfirmed");
            if ((receipt.Reason == "work_native_validation_required" || receipt.Reason == "work_native_build_validation_required")) return T("work.unavailable");
            return receipt.Accepted ? T(receipt.DryRun ? "work.pending" : "work.confirmed") : T("work.rejected");
        }
        internal void Close()
        { Deactivate(); if (root != null) { root.SetActive(false); Destroy(root); } }
        public override bool IsModal() { return true; }
        public override float GetSortKey() { return MODAL_SCREEN_SORT_KEY + 6; }
        public override void OnKeyDown(KButtonEvent e)
        { if (e.TryConsume(global::Action.Escape)) Close(); e.Consumed = true; }
        public override void OnKeyUp(KButtonEvent e) { e.Consumed = true; }
    }
}

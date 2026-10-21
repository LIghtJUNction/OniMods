using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Network;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal sealed class TeamColonyWindow : KScreen
    {
        private const int PageSize = 8;
        private sealed class PlayerRow
        {
            internal Button Button;
            internal TextMeshProUGUI Label;
            internal string ActorId;
        }
        private sealed class DuplicantRow
        {
            internal Button Button;
            internal TextMeshProUGUI Label;
            internal string EntityId;
        }
        private sealed class PlayerDisplay
        {
            internal string ActorId;
            internal string Name;
            internal int Number;
            internal bool Online;
            internal TeamColonyEntry Member;
        }
        private readonly List<PlayerRow> rows = new List<PlayerRow>();
        private readonly List<DuplicantRow> duplicantRows = new List<DuplicantRow>();
        private readonly Queue<string> delegationBatch = new Queue<string>();
        private readonly Dictionary<string, string> duplicantNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerInfo> rememberedPlayers = new Dictionary<string, PlayerInfo>(StringComparer.Ordinal);
        private OnlineRuntime runtime;
        private RoomSession session;
        private TeamColonyCoordinator team;
        private GameObject root;
        private Transform rosterBody;
        private TextMeshProUGUI status;
        private TextMeshProUGUI message;
        private TextMeshProUGUI pageLabel;
        private TextMeshProUGUI selectedLabel;
        private TextMeshProUGUI selectedStatus;
        private TextMeshProUGUI permissionHint;
        private Button create;
        private Button accept;
        private Button leave;
        private Button previous;
        private Button next;
        private Button invite;
        private Button viewPlans;
        private Button issueTasks;
        private Button commandDupes;
        private Button administrator;
        private Button transfer;
        private Button membersTab;
        private Button duplicantsTab;
        private GameObject memberPane;
        private GameObject duplicantPane;
        private TextMeshProUGUI duplicantSummary;
        private TextMeshProUGUI duplicantPageLabel;
        private TextMeshProUGUI selectedDuplicantLabel;
        private TextMeshProUGUI selectedDuplicantStatus;
        private TextMeshProUGUI duplicantEmpty;
        private Button previousDuplicants;
        private Button nextDuplicants;
        private Button delegateSelected;
        private Button revokeSelected;
        private Button delegateAll;
        private Button revokeAll;
        private Button cancelBatch;
        private bool showingDuplicants;
        private bool batchAllow;
        private bool batchHasSubmitted;
        private string batchPlanet;
        private string batchEpoch;
        private string selectedDuplicant;
        private int duplicantPage;
        private string selectedActor;
        private int page;
        private bool dirty = true;
        private bool closing;

        internal static TeamColonyWindow Create(OnlineRuntime runtime)
        {
            if (runtime == null || runtime.Session == null || runtime.Multiplayer?.Teams == null)
                throw new InvalidOperationException("team_unavailable");
            var root = OnlineWidgets.CanvasRoot("OniOnlineTeam", 30030);
            var shade = OnlineWidgets.Rect("Shade", root.transform);
            OnlineWidgets.Stretch((RectTransform)shade.transform);
            OnlineWidgets.Surface(shade, new Color(0, 0, 0, .65f));
            var panel = OnlineWidgets.Rect("TeamPanel", root.transform);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(.06f, .08f); rect.anchorMax = new Vector2(.94f, .92f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            OnlineWidgets.Surface(panel, OnlineWidgets.PanelColor, true);
            var window = panel.AddComponent<TeamColonyWindow>();
            window.runtime = runtime; window.root = root; window.session = runtime.Session; window.team = runtime.Multiplayer.Teams;
            window.team.Changed += window.MarkDirty;
            window.session.Changed += window.MarkDirty;
            window.Build(); window.Activate(); return window;
        }
        private static string T(string key) { return OnlineText.Get(key); }
        private static string F(string key, params object[] values)
        { return string.Format(CultureInfo.CurrentCulture, T(key), values); }
        private void Build()
        {
            var body = OnlineWidgets.Column(transform, "TeamBody", 14, 9);
            OnlineWidgets.Stretch((RectTransform)body);
            var header = OnlineWidgets.Row(body, 40);
            OnlineWidgets.Surface(header.gameObject, OnlineWidgets.AccentColor, true);
            header.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 4, 3, 3);
            var title = OnlineWidgets.Label(header, T("team.title"), 21);
            OnlineWidgets.Size(title.gameObject, -1, 40, 1);
            OnlineWidgets.Button(header, T("ui.close"), Close, 88);
            status = Caption(body, "", 17, 42);
            var actions = OnlineWidgets.Row(body, 40);
            create = OnlineWidgets.Button(actions, T("team.create"), () => Run(() => team.CreateTeam()), -1);
            accept = OnlineWidgets.Button(actions, T("team.accept"), () => Run(() => team.AcceptInvitation()), -1);
            leave = OnlineWidgets.Button(actions, T("team.leave"), () => Run(() => team.LeaveTeam()), -1);

            var tabs = OnlineWidgets.Row(body, 34);
            membersTab = OnlineWidgets.Button(tabs, T("team.members_tab"), () => SelectTab(false), -1);
            duplicantsTab = OnlineWidgets.Button(tabs, T("team.my_dupes_tab"), () => SelectTab(true), -1);
            OnlineWidgets.Size(membersTab.gameObject, -1, 34, 1);
            OnlineWidgets.Size(duplicantsTab.gameObject, -1, 34, 1);
            var columns = OnlineWidgets.Rect("RosterAndRoles", body);
            memberPane = columns;
            OnlineWidgets.Size(columns, -1, 300).flexibleHeight = 1;
            var group = columns.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 14; group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false; group.childForceExpandHeight = true;
            var left = OnlineWidgets.Column(columns.transform, "Players", 0, 8);
            OnlineWidgets.Size(left.gameObject, 390, 0).flexibleHeight = 1;
            OnlineWidgets.SectionHeader(left, T("team.players"));
            var scroll = OnlineWidgets.Scroll(left, 260, out rosterBody);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            for (int i = 0; i < PageSize; i++)
            {
                var row = new PlayerRow();
                row.Button = OnlineWidgets.Button(rosterBody, "", () => { selectedActor = row.ActorId; Refresh(); }, -1);
                OnlineWidgets.Size(row.Button.gameObject, -1, 58);
                row.Label = row.Button.GetComponentInChildren<TextMeshProUGUI>();
                row.Label.fontSize = 16; row.Label.alignment = TextAlignmentOptions.MidlineLeft;
                row.Label.textWrappingMode = TextWrappingModes.Normal;
                rows.Add(row);
            }
            var pages = OnlineWidgets.Row(left, 36);
            previous = OnlineWidgets.Button(pages, T("team.previous"), () => { page = Math.Max(0, page - 1); Refresh(); }, 98);
            pageLabel = OnlineWidgets.Label(pages, "", 16);
            pageLabel.alignment = TextAlignmentOptions.Center;
            OnlineWidgets.Size(pageLabel.gameObject, -1, 36, 1);
            next = OnlineWidgets.Button(pages, T("team.next"), () => { page++; Refresh(); }, 98);

            var right = OnlineWidgets.Column(columns.transform, "Permissions", 0, 6);
            OnlineWidgets.Size(right.gameObject, -1, 0, 1).flexibleHeight = 1;
            OnlineWidgets.SectionHeader(right, T("team.permissions"));
            selectedLabel = Caption(right, T("team.select_player"), 20, 28);
            selectedStatus = Caption(right, "", 16, 24);
            invite = OnlineWidgets.Button(right, T("team.invite"), () => Run(() => team.Invite(selectedActor)), -1);
            OnlineWidgets.Size(invite.gameObject, -1, 34, 1);
            viewPlans = PermissionButton(right, TeamPermissionFlags.ViewPlans);
            issueTasks = PermissionButton(right, TeamPermissionFlags.IssueTasks);
            commandDupes = PermissionButton(right, TeamPermissionFlags.CommandDuplicants);
            var management = OnlineWidgets.Row(right, 34);
            administrator = OnlineWidgets.Button(management, T("team.administrator"), ToggleAdministrator, -1);
            transfer = OnlineWidgets.Button(management, T("team.transfer"), () => Run(() => team.TransferAdministration(selectedActor)), -1);
            OnlineWidgets.Size(administrator.gameObject, -1, 34, 1);
            OnlineWidgets.Size(transfer.gameObject, -1, 34, 1);
            permissionHint = Caption(right, T("team.permission_hint"), 15, 54);
            permissionHint.color = OnlineWidgets.MutedColor;
            OnlineWidgets.Space(right, 0, true);
            BuildDuplicantPane(body);
            message = Caption(body, "", 15, 38);
            message.color = OnlineWidgets.MutedColor;
            var limitations = Caption(body, T("team.native_notice") + "\n" + T("team.host_notice"), 15, 58);
            limitations.color = OnlineWidgets.MutedColor;
            Refresh();
        }
        private void BuildDuplicantPane(Transform body)
        {
            duplicantPane = OnlineWidgets.Rect("OwnedDuplicantDelegation", body);
            OnlineWidgets.Size(duplicantPane, -1, 300).flexibleHeight = 1;
            var group = duplicantPane.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 14; group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false; group.childForceExpandHeight = true;
            var left = OnlineWidgets.Column(duplicantPane.transform, "OwnedDuplicants", 0, 8);
            OnlineWidgets.Size(left.gameObject, 390, 0).flexibleHeight = 1;
            OnlineWidgets.SectionHeader(left, T("team.my_dupes_tab"));
            duplicantSummary = Caption(left, "", 15, 42);
            Transform contents;
            var scroll = OnlineWidgets.Scroll(left, 200, out contents);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            duplicantEmpty = Caption(contents, T("team.no_owned_dupes"), 16, 76);
            duplicantEmpty.color = OnlineWidgets.MutedColor;
            for (int i = 0; i < PageSize; i++)
            {
                var row = new DuplicantRow();
                row.Button = OnlineWidgets.Button(contents, "", () => { selectedDuplicant = row.EntityId; Refresh(); }, -1);
                OnlineWidgets.Size(row.Button.gameObject, -1, 58);
                row.Label = row.Button.GetComponentInChildren<TextMeshProUGUI>();
                row.Label.fontSize = 16; row.Label.alignment = TextAlignmentOptions.MidlineLeft;
                row.Label.textWrappingMode = TextWrappingModes.Normal;
                duplicantRows.Add(row);
            }
            var pages = OnlineWidgets.Row(left, 36);
            previousDuplicants = OnlineWidgets.Button(pages, T("team.previous"), () => { duplicantPage = Math.Max(0, duplicantPage - 1); Refresh(); }, 98);
            duplicantPageLabel = OnlineWidgets.Label(pages, "", 16);
            duplicantPageLabel.alignment = TextAlignmentOptions.Center;
            OnlineWidgets.Size(duplicantPageLabel.gameObject, -1, 36, 1);
            nextDuplicants = OnlineWidgets.Button(pages, T("team.next"), () => { duplicantPage++; Refresh(); }, 98);
            var right = OnlineWidgets.Column(duplicantPane.transform, "DelegationControls", 0, 6);
            OnlineWidgets.Size(right.gameObject, -1, 0, 1).flexibleHeight = 1;
            OnlineWidgets.SectionHeader(right, T("team.dupe_delegation"));
            selectedDuplicantLabel = Caption(right, "", 20, 30);
            selectedDuplicantStatus = Caption(right, "", 16, 28);
            delegateSelected = OnlineWidgets.Button(right, T("team.delegate_selected"), () => SetSelectedDelegation(true), -1);
            revokeSelected = OnlineWidgets.Button(right, T("team.revoke_selected"), () => SetSelectedDelegation(false), -1);
            var batch = OnlineWidgets.Row(right, 36);
            delegateAll = OnlineWidgets.Button(batch, T("team.delegate_all_owned"), () => BeginDelegationBatch(true), -1);
            revokeAll = OnlineWidgets.Button(batch, T("team.revoke_all_owned"), () => BeginDelegationBatch(false), -1);
            cancelBatch = OnlineWidgets.Button(right, T("team.cancel_delegation_batch"), () => { delegationBatch.Clear(); Refresh(); }, -1);
            foreach (var button in new[] { delegateSelected, revokeSelected, delegateAll, revokeAll, cancelBatch })
                OnlineWidgets.Size(button.gameObject, -1, 36, 1);
            var hint = Caption(right, T("team.dupe_owner_hint"), 15, 88);
            hint.color = OnlineWidgets.MutedColor;
            OnlineWidgets.Space(right, 0, true);
            duplicantPane.SetActive(false);
        }
        private void SelectTab(bool duplicants)
        { showingDuplicants = duplicants; Refresh(); }
        private TeamColonyEntry[] OwnRegisteredDuplicants(TeamColonySnapshot state)
        {
            return (state?.Entries ?? Array.Empty<TeamColonyEntry>()).Where(e =>
                e.Kind == TeamColonyEntryKind.Duplicant && e.ActorId == session.LocalPlayerId)
                .GroupBy(e => e.EntityId, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(e => e.EntityId, StringComparer.Ordinal).ToArray();
        }
        private void SetSelectedDelegation(bool allow)
        {
            var state = team.Snapshot;
            var own = OwnRegisteredDuplicants(state).FirstOrDefault(d => d.EntityId == selectedDuplicant);
            if (state?.IsMember != true || !state.HostAvailable || own == null || delegationBatch.Count > 0 || team.PendingCount > 0) return;
            Run(() => team.DelegateDuplicantToTeam(own.EntityId, allow));
        }
        private void BeginDelegationBatch(bool allow)
        {
            var state = team.Snapshot;
            if (state?.IsMember != true || !state.HostAvailable || delegationBatch.Count > 0 || team.PendingCount > 0) return;
            foreach (var own in OwnRegisteredDuplicants(state).Where(d => d.Enabled != allow)) delegationBatch.Enqueue(own.EntityId);
            batchAllow = allow; batchHasSubmitted = false; batchPlanet = state.PlanetId; batchEpoch = state.AuthorityEpoch;
            ProcessDelegationBatch(); Refresh();
        }
        private void ProcessDelegationBatch()
        {
            if (delegationBatch.Count == 0 || team.PendingCount > 0) return;
            if (batchHasSubmitted && !string.IsNullOrEmpty(team.LastError))
            { delegationBatch.Clear(); dirty = true; return; }
            var state = team.Snapshot;
            if (state == null || !state.IsMember || state.PlanetId != batchPlanet || state.AuthorityEpoch != batchEpoch)
            { delegationBatch.Clear(); dirty = true; return; }
            if (!state.HostAvailable) return;
            string id = delegationBatch.Dequeue();
            var own = OwnRegisteredDuplicants(state).FirstOrDefault(d => d.EntityId == id);
            if (own != null && own.Enabled != batchAllow)
            {
                if (team.DelegateDuplicantToTeam(id, batchAllow)) batchHasSubmitted = true;
                else delegationBatch.Clear();
            }
            dirty = true;
        }
        private void RefreshDuplicants(TeamColonySnapshot state, bool available)
        {
            var own = OwnRegisteredDuplicants(state);
            if (!own.Any(d => d.EntityId == selectedDuplicant)) selectedDuplicant = own.FirstOrDefault()?.EntityId;
            int pages = Math.Max(1, (own.Length + PageSize - 1) / PageSize);
            duplicantPage = Math.Min(duplicantPage, pages - 1);
            previousDuplicants.interactable = duplicantPage > 0; nextDuplicants.interactable = duplicantPage + 1 < pages;
            duplicantPageLabel.text = (duplicantPage + 1) + " / " + pages;
            duplicantSummary.text = F("team.owned_dupe_count", own.Length, own.Count(d => d.Enabled));
            duplicantEmpty.gameObject.SetActive(own.Length == 0);
            RefreshNativeDuplicantNames(state);
            for (int i = 0; i < duplicantRows.Count; i++)
            {
                int index = duplicantPage * PageSize + i;
                var row = duplicantRows[i]; row.Button.gameObject.SetActive(index < own.Length);
                if (index >= own.Length) continue;
                var item = own[index]; row.EntityId = item.EntityId;
                row.Label.text = DuplicantName(item.EntityId, index) + "\n" + T(item.Enabled ? "team.dupe_delegated" : "team.dupe_not_delegated");
                row.Label.color = item.Enabled ? OnlineWidgets.OnlineColor : OnlineWidgets.TextColor;
                OnlineWidgets.Tint(row.Button, item.EntityId == selectedDuplicant ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            }
            var selected = own.FirstOrDefault(d => d.EntityId == selectedDuplicant);
            selectedDuplicantLabel.text = selected == null ? T("team.select_owned_dupe") : DuplicantName(selected.EntityId, Array.IndexOf(own, selected));
            selectedDuplicantStatus.text = selected == null ? "" : T(selected.Enabled ? "team.dupe_delegated" : "team.dupe_not_delegated");
            bool canDelegate = available && state?.IsMember == true && team.PendingCount == 0 && delegationBatch.Count == 0;
            delegateSelected.interactable = canDelegate && selected != null && !selected.Enabled;
            revokeSelected.interactable = canDelegate && selected?.Enabled == true;
            delegateAll.interactable = canDelegate && own.Any(d => !d.Enabled);
            revokeAll.interactable = canDelegate && own.Any(d => d.Enabled);
            cancelBatch.interactable = delegationBatch.Count > 0;
        }
        private void RefreshNativeDuplicantNames(TeamColonySnapshot state)
        {
            duplicantNames.Clear();
            if (!session.IsHost || state?.HostAvailable != true || Game.Instance == null || runtime.SelectedPlanet?.PlanetId != state.PlanetId) return;
            foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null) continue;
                var id = dupe.GetComponent<KPrefabID>();
                if (id == null || id.InstanceID < 0) continue;
                duplicantNames[id.InstanceID.ToString(CultureInfo.InvariantCulture)] =
                    System.Text.RegularExpressions.Regex.Replace(dupe.GetProperName() ?? "", "<[^>]*>", "");
            }
        }
        private string DuplicantName(string id, int index)
        { string name; return duplicantNames.TryGetValue(id, out name) && !string.IsNullOrEmpty(name) ? name : F("team.registered_dupe_number", index + 1); }
        private static TextMeshProUGUI Caption(Transform parent, string text, int size, int height)
        {
            var label = OnlineWidgets.Label(parent, text, size);
            OnlineWidgets.Size(label.gameObject, -1, height);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }
        private Button PermissionButton(Transform parent, TeamPermissionFlags flag)
        {
            var button = OnlineWidgets.Button(parent, "", () => TogglePermission(flag), -1);
            OnlineWidgets.Size(button.gameObject, -1, 34, 1);
            return button;
        }
        private void TogglePermission(TeamPermissionFlags flag)
        {
            var member = team.Snapshot?.Members.FirstOrDefault(m => m.ActorId == selectedActor);
            if (member != null) Run(() => team.SetPermissions(selectedActor, member.Permissions ^ flag));
        }
        private void ToggleAdministrator()
        {
            var member = team.Snapshot?.Members.FirstOrDefault(m => m.ActorId == selectedActor);
            if (member != null) Run(() => team.SetAdministrator(selectedActor, !member.Administrator));
        }
        private void Run(System.Func<bool> operation)
        {
            operation(); dirty = true; Refresh();
        }
        private void MarkDirty() { dirty = true; }
        private void Update()
        {
            if (closing) return;
            if (runtime == null || runtime.Session != session || runtime.Multiplayer?.Teams != team) { Close(); return; }
            ProcessDelegationBatch();
            if (dirty) Refresh();
        }
        private void Refresh()
        {
            if (closing || team == null || session == null) return;
            dirty = false;
            var state = team.Snapshot;
            memberPane.SetActive(!showingDuplicants); duplicantPane.SetActive(showingDuplicants);
            OnlineWidgets.Tint(membersTab, showingDuplicants ? OnlineWidgets.ButtonColor : OnlineWidgets.AccentColor);
            OnlineWidgets.Tint(duplicantsTab, showingDuplicants ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            var members = state?.Members ?? Array.Empty<TeamColonyEntry>();
            var self = members.FirstOrDefault(m => m.ActorId == session.LocalPlayerId);
            bool connected = session.Joined && !session.Reconnecting && !session.Leaving && !session.Closed;
            bool available = connected && state?.HostAvailable == true;
            bool canManage = available && self?.Administrator == true && team.PendingCount == 0 && delegationBatch.Count == 0;
            bool invited = state?.Invited == true;
            bool member = state?.IsMember == true;
            status.text = state == null || string.IsNullOrEmpty(state.TeamId) ? T("team.state_none") :
                !available ? T("team.state_stopped") : invited ? T("team.state_invited") :
                state.CanContinue ? F("team.state_running", members.Count(m => m.Online)) : T("team.state_waiting");
            status.color = available && state?.CanContinue == true ? OnlineWidgets.OnlineColor : OnlineWidgets.MutedColor;
            create.interactable = connected && session.IsHost && runtime.Multiplayer.StarSystem.Admitted &&
                state == null;
            accept.interactable = available && invited && !member && team.PendingCount == 0 && delegationBatch.Count == 0;
            int administratorCount = members.Count(m => m.Administrator);
            leave.interactable = available && member && team.PendingCount == 0 && delegationBatch.Count == 0 && !(self?.Administrator == true && administratorCount == 1);

            var players = CollectPlayers(members);
            if (!players.Any(p => p.ActorId == selectedActor))
                selectedActor = players.FirstOrDefault(p => p.ActorId != session.LocalPlayerId)?.ActorId ?? players.FirstOrDefault()?.ActorId;
            int pages = Math.Max(1, (players.Count + PageSize - 1) / PageSize);
            page = Math.Min(page, pages - 1);
            pageLabel.text = (page + 1) + " / " + pages;
            previous.interactable = page > 0; next.interactable = page + 1 < pages;
            for (int i = 0; i < rows.Count; i++)
            {
                int index = page * PageSize + i;
                bool visible = index < players.Count;
                var row = rows[i]; row.Button.gameObject.SetActive(visible);
                if (!visible) continue;
                var player = players[index]; row.ActorId = player.ActorId;
                row.Label.text = DisplayName(player) + "\n" + PlayerStatus(player);
                row.Label.color = player.Online ? OnlineWidgets.PlayerColor(player.Number) : OnlineWidgets.MutedColor;
                OnlineWidgets.Tint(row.Button, player.ActorId == selectedActor ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            }
            var selected = players.FirstOrDefault(p => p.ActorId == selectedActor);
            selectedLabel.text = selected == null ? T("team.select_player") : DisplayName(selected);
            selectedLabel.color = selected == null ? OnlineWidgets.TextColor : OnlineWidgets.PlayerColor(selected.Number);
            selectedStatus.text = selected == null ? "" : PlayerStatus(selected);
            invite.interactable = canManage && selected != null && selected.Online && selected.Member == null;
            bool edit = canManage && selected?.Member != null;
            BindPermission(viewPlans, "team.view_plans", selected?.Member, TeamPermissionFlags.ViewPlans, edit);
            BindPermission(issueTasks, "team.issue_tasks", selected?.Member, TeamPermissionFlags.IssueTasks, edit);
            BindPermission(commandDupes, "team.command_dupes", selected?.Member, TeamPermissionFlags.CommandDuplicants, edit);
            bool isAdministrator = selected?.Member?.Administrator == true;
            ButtonText(administrator, T("team.administrator") + " · " + T(isAdministrator ? "ui.enabled" : "ui.disabled"));
            administrator.interactable = edit && !(isAdministrator && administratorCount == 1);
            transfer.interactable = edit && selected.ActorId != session.LocalPlayerId && selected.Online;
            permissionHint.text = T("team.permission_hint") + (self?.Administrator == true && administratorCount == 1 ? "\n" + T("team.last_administrator") : "");
            RefreshDuplicants(state, available);
            message.text = delegationBatch.Count > 0 ? F("team.delegation_batch_pending", delegationBatch.Count) : team.PendingCount > 0 ? T("team.pending") : string.IsNullOrEmpty(team.LastError) ?
                (!canManage && member ? T("team.admin_only") : "") : ErrorText(team.LastError);
        }
        private List<PlayerDisplay> CollectPlayers(TeamColonyEntry[] members)
        {
            foreach (var player in session.Players) rememberedPlayers[player.Id] = player;
            var current = session.Players;
            var ids = current.Select(p => p.Id).Union(members.Select(m => m.ActorId), StringComparer.Ordinal);
            return ids.Select(id =>
            {
                PlayerInfo player; rememberedPlayers.TryGetValue(id, out player);
                return new PlayerDisplay { ActorId = id, Name = player?.Name ?? T("team.offline_member"), Number = player?.Number ?? 0,
                    Online = current.Any(p => p.Id == id && p.Connected), Member = members.FirstOrDefault(m => m.ActorId == id) };
            }).OrderBy(p => p.Member == null ? 1 : 0).ThenBy(p => p.Number <= 0 ? int.MaxValue : p.Number)
                .ThenBy(p => p.ActorId, StringComparer.Ordinal).ToList();
        }
        private static string DisplayName(PlayerDisplay player)
        { return (player.Number > 0 ? "#" + player.Number + "  " : "") + player.Name; }
        private static string PlayerStatus(PlayerDisplay player)
        {
            return T(player.Member == null ? "team.nonmember" : player.Member.Administrator ? "team.administrator" : "team.member")
                + " · " + T(player.Online ? "team.online" : "team.offline");
        }
        private static void BindPermission(Button button, string key, TeamColonyEntry member, TeamPermissionFlags flag, bool editable)
        {
            bool enabled = member != null && (member.Permissions & flag) != 0;
            ButtonText(button, T(key) + " · " + T(enabled ? "ui.enabled" : "ui.disabled"));
            button.interactable = editable;
            OnlineWidgets.Tint(button, enabled ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
        }
        private static void ButtonText(Button button, string text)
        { button.GetComponentInChildren<TextMeshProUGUI>().text = text; }
        private static string ErrorText(string reason)
        {
            switch (reason)
            {
                case "team_host_native_only": return T("team.error_host_native");
                case "team_existing_authority_changed": return T("team.error_authority_changed");
                case "team_pending_budget": return T("team.error_budget");
                case "team_receipt_budget": return T("team.error_receipt_budget");
                case "team_denied_or_stale": case "team_conflicting_retry": return T("team.error_denied");
                default: return T("team.error_unavailable");
            }
        }
        internal void Close()
        {
            if (closing) return;
            closing = true; delegationBatch.Clear(); Unsubscribe(); Deactivate();
            if (root != null) { root.SetActive(false); Destroy(root); }
        }
        private void Unsubscribe()
        {
            if (team != null) team.Changed -= MarkDirty;
            if (session != null) session.Changed -= MarkDirty;
        }
        protected override void OnCleanUp() { Unsubscribe(); base.OnCleanUp(); }
        public override bool IsModal() { return true; }
        public override float GetSortKey() { return MODAL_SCREEN_SORT_KEY + 8; }
        public override void OnKeyDown(KButtonEvent e)
        { if (e.TryConsume(global::Action.Escape)) Close(); e.Consumed = true; }
        public override void OnKeyUp(KButtonEvent e) { e.Consumed = true; }
    }
}

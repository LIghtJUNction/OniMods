using System.Collections.Generic;
using System.Linq;
using OniOnline.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // Remote entries never become WorldContainers or native numeric world IDs.
    // Native rows keep their camera switch; remote rows open an authorized observation.
    internal sealed class OnlineWorldSelector : MonoBehaviour
    {
        private WorldSelector selector;
        private readonly Dictionary<string, MultiToggle> remoteRows = new Dictionary<string, MultiToggle>();
        private int revision = -1;
        internal static void Attach(WorldSelector selector)
        {
            var component = selector.GetComponent<OnlineWorldSelector>();
            if (component == null) { component = selector.gameObject.AddComponent<OnlineWorldSelector>(); component.selector = selector; }
            component.Refresh();
        }
        private void Update()
        {
            if (OnlineRuntime.Instance != null && revision != OnlineRuntime.Instance.Revision) Refresh();
        }
        private static string Name(string player, string planet)
        { return string.Format(OnlineText.Get("planet.owned_name"), player, planet); }
        private void Refresh()
        {
            var runtime = OnlineRuntime.Instance;
            if (runtime == null || selector.worldRows == null) return;
            revision = runtime.Revision;
            foreach (var row in selector.worldRows)
            {
                var world = ClusterManager.Instance.GetWorld(row.Key);
                if (world == null || world.IsModuleInterior) continue;
                var label = row.Value.GetComponent<HierarchyReferences>().GetReference<LocText>("Label");
                label.richText = false; label.SetText(Name(runtime.Nickname, world.GetProperName()));
            }
            var state = runtime.Multiplayer?.StarSystem.State;
            var members = state?.Members.Where(m => m.Planet != null && m.PeerId != runtime.Session?.LocalPlayerId).ToArray();
            foreach (var stale in remoteRows.Keys.Where(id => members == null || !members.Any(m => m.PeerId == id)).ToArray())
            { remoteRows[stale].gameObject.SetActive(false); Destroy(remoteRows[stale].gameObject); remoteRows.Remove(stale); }
            if (members == null) return;
            foreach (var member in members)
            {
                MultiToggle row;
                string owner = member.PeerId;
                if (!remoteRows.TryGetValue(owner, out row))
                {
                    row = Util.KInstantiateUI(selector.worldRowPrefab, selector.worldRowContainer).GetComponent<MultiToggle>();
                    row.gameObject.name = "OniOnlineRemoteAsteroid_" + owner;
                    foreach (var alert in row.GetComponentsInChildren<AlertVignette>(true))
                    { alert.enabled = false; if (alert.image != null) alert.image.color = Color.clear; }
                    row.onClick = () => runtime.ObserveWorld(owner);
                    remoteRows.Add(owner, row);
                }
                var player = runtime.Session.Players.FirstOrDefault(p => p.Id == owner);
                var references = row.GetComponent<HierarchyReferences>();
                var label = references.GetReference<LocText>("Label");
                label.richText = false; label.SetText(Name(player?.Name ?? "", member.Planet.Name));
                references.GetReference<Image>("Icon").sprite = Assets.GetSprite("unknown_far");
                var status = references.GetReference<LocText>("StatusLabel");
                status.richText = false; status.SetText(member.Connected ? "" : OnlineText.Get("ui.player_offline"));
                var tooltip = row.GetComponent<ToolTip>();
                if (tooltip != null)
                {
                    tooltip.ClearMultiStringTooltip();
                    tooltip.AddMultiStringTooltip(Name(player?.Name ?? "", member.Planet.Name), selector.titleTextSetting);
                    tooltip.AddMultiStringTooltip(OnlineText.Get("planet.remote_view"), selector.bodyTextSetting);
                }
                row.ChangeState(0); row.gameObject.SetActive(true); row.transform.SetAsLastSibling();
            }
        }
        private void OnDestroy()
        { foreach (var row in remoteRows.Values) if (row != null) Destroy(row.gameObject); remoteRows.Clear(); }
    }
}

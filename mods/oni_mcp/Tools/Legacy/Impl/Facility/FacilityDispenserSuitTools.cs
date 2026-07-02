using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using STRINGS;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        public static McpTool ListDispensers()
        {
            return new McpTool
            {
                Name = "dispensers_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "dispenser_side_screens_list", "pajama_dispensers_list" },
                Tags = new List<string> { "building", "dispenser", "side-screen", "pajamas" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=dispenser action=list",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId、物品名或 itemId 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListBuildingTargets(args, go => go.GetComponent<IDispenser>() != null, go => DispenserInfo(go), "dispensers")
            };
        }

        public static McpTool ControlDispenser()
        {
            return new McpTool
            {
                Name = "dispenser_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "dispenser_order", "pajama_dispenser_control" },
                Tags = new List<string> { "building", "dispenser", "side-screen", "pajamas" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=dispenser action=select_item/order/cancel",
                Hidden = true,
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "select_item、order 或 cancel", Required = true, EnumValues = new List<string> { "select_item", "order", "cancel" } },
                    ["itemId"] = new McpToolParameter { Type = "string", Description = "action=select_item 时的目标 Tag/prefab id；可省略并用 itemIndex", Required = false },
                    ["itemIndex"] = new McpToolParameter { Type = "integer", Description = "action=select_item 时 DispensedItems() 的序号", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, target => target.GetComponent<IDispenser>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target dispenser not found");
                    var dispenser = go.GetComponent<IDispenser>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = DispenserInfo(go);

                    if (action == "select_item")
                    {
                        Tag item = ResolveDispensedItem(args, dispenser);
                        if (!item.IsValid)
                            return CallToolResult.Error("itemId or itemIndex must match an available dispensed item");
                        dispenser.SelectItem(item);
                    }
                    else if (action == "order")
                    {
                        if (!dispenser.HasOpenChore())
                            dispenser.OnOrderDispense();
                    }
                    else if (action == "cancel")
                    {
                        if (dispenser.HasOpenChore())
                            dispenser.OnCancelDispense();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be select_item, order, or cancel");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["action"] = action,
                        ["before"] = before,
                        ["dispenser"] = DispenserInfo(go)
                    });
                }
            };
        }

        public static McpTool ListSuitLockers()
        {
            return new McpTool
            {
                Name = "suit_lockers_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "suit_locker_side_screens_list", "atmo_suit_lockers_list" },
                Tags = new List<string> { "building", "suit", "checkpoint", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=suit_locker action=list",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId、服装类型或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListBuildingTargets(args, go => go.GetComponent<SuitLocker>() != null, go => SuitLockerInfo(go.GetComponent<SuitLocker>()), "lockers")
            };
        }

        public static McpTool ControlSuitLocker()
        {
            return new McpTool
            {
                Name = "suit_locker_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "suit_locker_config", "suit_locker_drop_suit" },
                Tags = new List<string> { "building", "suit", "checkpoint", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=suit_locker action=request_suit/no_suit/drop_suit",
                Hidden = true,
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "request_suit、no_suit 或 drop_suit", Required = true, EnumValues = new List<string> { "request_suit", "no_suit", "drop_suit" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=drop_suit 必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, target => target.GetComponent<SuitLocker>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target SuitLocker not found");
                    var locker = go.GetComponent<SuitLocker>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = SuitLockerInfo(locker);

                    if (action == "request_suit")
                    {
                        locker.ConfigRequestSuit();
                    }
                    else if (action == "no_suit")
                    {
                        locker.ConfigNoSuit();
                    }
                    else if (action == "drop_suit")
                    {
                        if (!ToolUtil.GetBool(args, "confirm", false))
                            return CallToolResult.Error("confirm=true is required to drop a stored suit");
                        if (locker.GetStoredOutfit() == null)
                            return CallToolResult.Error("SuitLocker has no stored suit to drop");
                        locker.DropSuit();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be request_suit, no_suit, or drop_suit");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["action"] = action,
                        ["before"] = before,
                        ["locker"] = SuitLockerInfo(locker)
                    });
                }
            };
        }

        private static Dictionary<string, object> DispenserInfo(GameObject go)
        {
            var dispenser = go.GetComponent<IDispenser>();
            var result = TargetInfo(go);
            Tag selected = dispenser.SelectedItem();
            result["selectedItemId"] = selected.IsValid ? selected.Name : null;
            result["hasOpenChore"] = dispenser.HasOpenChore();
            result["items"] = dispenser.DispensedItems().Select((tag, index) => ItemInfo(tag, index, tag == selected)).ToList();
            return result;
        }

        private static Dictionary<string, object> ItemInfo(Tag tag, int index, bool selected)
        {
            var prefab = Assets.GetPrefab(tag);
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["itemId"] = tag.Name,
                ["name"] = prefab != null ? ToolUtil.CleanName(prefab.GetProperName()) : tag.ProperName(),
                ["selected"] = selected
            };
        }

        private static Tag ResolveDispensedItem(JObject args, IDispenser dispenser)
        {
            var items = dispenser.DispensedItems();
            int? index = ToolUtil.GetInt(args, "itemIndex");
            if (index.HasValue && index.Value >= 0 && index.Value < items.Count)
                return items[index.Value];
            string itemId = args["itemId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(itemId))
            {
                foreach (var item in items)
                {
                    if (string.Equals(item.Name, itemId.Trim(), StringComparison.OrdinalIgnoreCase))
                        return item;
                }
            }
            return Tag.Invalid;
        }

        private static Dictionary<string, object> SuitLockerInfo(SuitLocker locker)
        {
            var result = TargetInfo(locker.gameObject);
            var smi = locker.smi;
            var stored = locker.GetStoredOutfit();
            result["configured"] = smi != null && smi.sm.isConfigured.Get(smi);
            result["waitingForSuit"] = smi != null && smi.sm.isWaitingForSuit.Get(smi);
            result["hasStoredSuit"] = stored != null;
            result["storedSuit"] = stored == null ? null : StoredSuitInfo(stored);
            result["outfitTags"] = locker.OutfitTags?.Select(tag => tag.Name).ToList() ?? new List<string>();
            result["oxygenAvailable"] = stored == null ? null : TankPercent(stored.GetComponent<SuitTank>());
            result["batteryAvailable"] = stored == null ? null : TankPercent(stored.GetComponent<LeadSuitTank>());
            result["canDropSuit"] = stored != null;
            result["canRequestSuit"] = stored == null;
            result["canSetNoSuit"] = true;
            return result;
        }

        private static Dictionary<string, object> StoredSuitInfo(KPrefabID suit)
        {
            var tank = suit.GetComponent<SuitTank>();
            var jetTank = suit.GetComponent<JetSuitTank>();
            var leadTank = suit.GetComponent<LeadSuitTank>();
            return new Dictionary<string, object>
            {
                ["id"] = suit.InstanceID,
                ["prefabId"] = suit.PrefabTag.Name,
                ["name"] = ToolUtil.CleanName(suit.GetProperName()),
                ["oxygen"] = TankPercent(tank),
                ["jetSuitFuel"] = TankPercent(jetTank),
                ["battery"] = TankPercent(leadTank)
            };
        }

        private static object TankPercent(SuitTank tank)
        {
            return tank == null ? null : (object)Math.Round(ToolUtil.SafeFloat(tank.PercentFull()), 4);
        }

        private static object TankPercent(JetSuitTank tank)
        {
            return tank == null ? null : (object)Math.Round(ToolUtil.SafeFloat(tank.PercentFull()), 4);
        }

        private static object TankPercent(LeadSuitTank tank)
        {
            return tank == null ? null : (object)Math.Round(ToolUtil.SafeFloat(tank.PercentFull()), 4);
        }
    }
}

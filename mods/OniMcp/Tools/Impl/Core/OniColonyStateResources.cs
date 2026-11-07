using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    /// <summary>
    /// Registers and routes first-class colony state dynamic resources, templates, and tasks.
    /// </summary>
    public static class OniColonyStateResources
    {
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            OniResourceRegistry.RegisterResourceProvider(GetColonyStateResources);
            OniResourceRegistry.RegisterTemplateProvider(AddColonyStateResourceTemplates);
            OniResourceRegistry.RegisterDynamicRouteHandler(ReadColonyStateResourceRoutes);
        }

        public static IEnumerable<McpResourceInfo> GetColonyStateResources()
        {
            return new List<McpResourceInfo>
            {
                ResourceInfo("oni://atmosphere/summary", "大气摘要", "世界大气整体质量、氧气占比、主要气体分布及气温统计。"),
                ResourceInfo("oni://atmosphere/breathability", "可呼吸区域分析", "殖民地可呼吸格子数、氧气储量、二氧化碳及窒息风险评估。"),
                ResourceInfo("oni://fluid/liquid/summary", "液体库存摘要", "净水、污水、原油、盐水等液体存量、温度及分布。"),
                ResourceInfo("oni://fluid/gas/summary", "气体分布摘要", "世界主要气体质量及分布分布统计。"),
                ResourceInfo("oni://plumbing/summary", "管网基础设施总览", "液体管道与气体管道网格段数、畅通状态及布局统计。"),
                ResourceInfo("oni://thermal/summary", "温度与热量总览", "世界最高、最低与平均温度场，极端高温与极寒格统计。"),
                ResourceInfo("oni://food/summary", "食物储备与消耗", "全殖民地食物卡路里储备、人均可用天数及断粮风险评估。"),
                ResourceInfo("oni://farming/crops", "农业与作物状态", "种植箱与农砖作物数量、可收获数量及生长分布。"),
                ResourceInfo("oni://ranching/summary", "牧业与生物生态", "动物存活总数、按物种分布及生态概况。"),
                ResourceInfo("oni://dupes/vitals", "复制人体征与健康", "生命值、压力、饱食度、氧气、膀胱及生理指标。"),
                ResourceInfo("oni://dupes/morale", "复制人士气与压力", "复制人士气值、士气期望值、净士气差额及压力预警。"),
                ResourceInfo("oni://geysers/summary", "地热喷孔与间歇泉", "已探明喷孔、类型、喷发元素、温度及坐标。"),
                ResourceInfo("oni://research/progress", "科技研发进度", "当前研发科技、已解锁科技总数及进度状态。"),
                ResourceInfo("oni://resources/stockpile", "全殖民地物资储备", "按分类汇总的矿物、金属、原材料与工业存量。"),
                ResourceInfo("oni://tasks/list", "动态异步任务列表", "后台异步长耗时任务状态、执行进度及关联资源 URI。"),
                ResourceInfo("oni://reports/list", "动态报告资源列表", "已生成的异步任务报告、诊断分析及长耗时产出资源。")
            };
        }

        private static McpResourceInfo ResourceInfo(string uri, string name, string description, string mimeType = "application/json")
        {
            return new McpResourceInfo
            {
                Uri = uri,
                Name = name,
                Description = description,
                MimeType = mimeType
            };
        }

        public static void AddColonyStateResourceTemplates(List<McpResourceTemplateInfo> templates)
        {
            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tasks/{taskId}",
                Name = "动态异步任务状态",
                Description = "读取后台异步任务的执行状态、进度与结果资源 URI。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://reports/{reportId}",
                Name = "动态任务报告资源",
                Description = "读取异步任务执行生成的最终详细报告数据。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://notes/{path}",
                Name = "存档隔离游戏笔记",
                Description = "读取当前活跃存档隔离的游戏笔记（如 AGENTS.md 或自定义进度笔记）。",
                MimeType = "text/markdown"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://blueprints/{name}",
                Name = "蓝图数据",
                Description = "读取已保存蓝图的完整建筑与材料结构 JSON 数据。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://atmosphere/summary{?worldId}",
                Name = "世界大气摘要（参数化）",
                Description = "按世界 ID 读取大气质量、氧气占比与主要气体分布。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://fluid/liquid/summary{?worldId}",
                Name = "世界液体库存（参数化）",
                Description = "按世界 ID 读取净水、污水、原油与各类液体储量与平均温度。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://thermal/summary{?worldId}",
                Name = "世界温度与热量总览（参数化）",
                Description = "按世界 ID 读取最高、最低与平均温度场，极端高低温格统计。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/crops{?worldId}",
                Name = "农业与作物状态（参数化）",
                Description = "按世界 ID 读取种植箱与农砖作物数量及可收获状态。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/summary{?worldId}",
                Name = "牧业与生物生态（参数化）",
                Description = "按世界 ID 读取动物存活总数及按物种分布统计。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://geysers/summary{?worldId}",
                Name = "地热喷孔与间歇泉（参数化）",
                Description = "按世界 ID 读取已探明喷孔、类型、喷发元素及坐标。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://plumbing/liquid{?worldId,networkId}",
                Name = "液体管道回路与流向（参数化）",
                Description = "按世界或回路 ID 读取液体管道网络、流动方向向量、管内元素/质量及堵塞瓶颈。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://plumbing/gas{?worldId,networkId}",
                Name = "气体管道回路与流向（参数化）",
                Description = "按世界或回路 ID 读取气体管道网络、流动方向向量、管内元素/质量及流动状态。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://circuits/logic{?worldId,circuitId}",
                Name = "自动化逻辑信号网络（参数化）",
                Description = "按世界或网络 ID 读取逻辑电路状态（红绿信号、4-bit线束通道）与自动化设备。",
                MimeType = "application/json"
            });

            templates.Add(new McpResourceTemplateInfo
            {
                UriTemplate = "oni://circuits/power{?worldId}",
                Name = "电网状态总览（参数化）",
                Description = "按世界 ID 读取所有电路的发电机额定输出、消费者负载、电池电量及过载状态。",
                MimeType = "application/json"
            });
        }

        public static ReadResourceResult ReadColonyStateResourceRoutes(string uri, Uri parsed)
        {
            if (parsed == null || parsed.Scheme != "oni")
                return null;

            int worldId = -1;
            int networkId = -1;
            int circuitId = -1;
            if (!string.IsNullOrEmpty(parsed.Query))
            {
                var query = parsed.Query.TrimStart('?').Split('&');
                foreach (var q in query)
                {
                    var kv = q.Split('=');
                    if (kv.Length == 2)
                    {
                        if (kv[0] == "worldId" && int.TryParse(kv[1], out int wid))
                            worldId = wid;
                        else if (kv[0] == "networkId" && int.TryParse(kv[1], out int nid))
                            networkId = nid;
                        else if (kv[0] == "circuitId" && int.TryParse(kv[1], out int cid))
                            circuitId = cid;
                    }
                }
            }

            string host = parsed.Host.ToLowerInvariant();
            string path = parsed.AbsolutePath.TrimEnd('/');

            if (host == "atmosphere")
            {
                if (path == "/summary")
                    return JsonResource(uri, ColonyStateDataCollector.BuildAtmosphereSummary(worldId));
                if (path == "/breathability")
                    return JsonResource(uri, ColonyStateDataCollector.BuildBreathabilityAnalysis(worldId));
            }
            else if (host == "fluid")
            {
                if (path == "/liquid/summary")
                    return JsonResource(uri, ColonyStateDataCollector.BuildLiquidSummary(worldId));
                if (path == "/gas/summary")
                    return JsonResource(uri, ColonyStateDataCollector.BuildAtmosphereSummary(worldId));
            }
            else if (host == "plumbing")
            {
                if (path == "/summary")
                    return JsonResource(uri, ColonyStateDataCollector.BuildPlumbingSummary(worldId));
                if (path == "/liquid")
                    return JsonResource(uri, ColonyPlumbingLogicDataCollector.BuildPlumbingDetailed(ConduitType.Liquid, worldId, networkId));
                if (path == "/gas")
                    return JsonResource(uri, ColonyPlumbingLogicDataCollector.BuildPlumbingDetailed(ConduitType.Gas, worldId, networkId));
                if (path.StartsWith("/cell/"))
                {
                    int cell = ParseCellCoord(path.Substring(6));
                    return JsonResource(uri, ColonyPlumbingLogicDataCollector.BuildConduitCellDetail(cell, ConduitType.Liquid));
                }
            }
            else if (host == "circuits")
            {
                if (path == "/logic")
                    return JsonResource(uri, ColonyPlumbingLogicDataCollector.BuildLogicCircuitsDetailed(worldId, circuitId));
                if (path == "/power")
                    return JsonResource(uri, ColonyPlumbingLogicDataCollector.BuildPowerCircuitsDetailed(worldId));
                if (path.StartsWith("/logic/cell/"))
                {
                    int cell = ParseCellCoord(path.Substring(12));
                    return JsonResource(uri, ColonyPlumbingLogicDataCollector.BuildLogicCellDetail(cell));
                }
            }
            else if (host == "thermal" && path == "/summary")
            {
                return JsonResource(uri, ColonyStateDataCollector.BuildThermalSummary(worldId));
            }
            else if (host == "food" && path == "/summary")
            {
                return JsonResource(uri, ColonyStateDataCollector.BuildFoodSummary(worldId));
            }
            else if (host == "farming" && path == "/crops")
            {
                return JsonResource(uri, ColonyStateDataCollector.BuildCropsSummary(worldId));
            }
            else if (host == "ranching" && path == "/summary")
            {
                return JsonResource(uri, ColonyStateDataCollector.BuildRanchingSummary(worldId));
            }
            else if (host == "dupes")
            {
                if (path == "/vitals")
                    return JsonResource(uri, ColonyStateDataCollector.BuildDupesVitals());
                if (path == "/morale")
                    return JsonResource(uri, ColonyStateDataCollector.BuildDupesMorale());
            }
            else if (host == "geysers" && path == "/summary")
            {
                return JsonResource(uri, ColonyStateDataCollector.BuildGeysersSummary(worldId));
            }
            else if (host == "research" && path == "/progress")
            {
                return JsonResource(uri, ColonyStateDataCollector.BuildResearchProgress());
            }
            else if (host == "resources" && path == "/stockpile")
            {
                return JsonResource(uri, ColonyStateDataCollector.BuildStockpileSummary(worldId));
            }
            else if (host == "tasks")
            {
                if (path == "/list" || string.IsNullOrEmpty(path))
                    return JsonResource(uri, OniDynamicTaskRegistry.ListTasks());
                string taskId = path.TrimStart('/');
                var task = OniDynamicTaskRegistry.GetTask(taskId);
                if (task != null)
                    return JsonResource(uri, task.ToJson());
            }
            else if (host == "reports")
            {
                if (path == "/list" || string.IsNullOrEmpty(path))
                    return JsonResource(uri, OniDynamicTaskRegistry.ListReports());
                string reportId = path.TrimStart('/');
                var report = OniDynamicTaskRegistry.GetReport(reportId);
                if (report != null)
                    return JsonResource(uri, report);
            }
            else if (host == "notes")
            {
                string notePath = path.TrimStart('/');
                if (string.IsNullOrEmpty(notePath)) notePath = "AGENTS.md";
                string content = SaveNotesManager.ReadNoteContent(notePath);
                if (content != null)
                    return TextResource(uri, content, "text/markdown");
            }
            else if (host == "blueprints")
            {
                string bpName = path.TrimStart('/');
                if (!string.IsNullOrEmpty(bpName))
                {
                    string json = WorldEditorTools.ReadBlueprintContent(bpName);
                    if (json != null)
                        return TextResource(uri, json, "application/json");
                }
            }

            return null;
        }

        private static ReadResourceResult JsonResource(string uri, JToken data)
        {
            if (data == null) return null;
            return new ReadResourceResult
            {
                Contents = new List<TextResourceContent>
                {
                    new TextResourceContent
                    {
                        Uri = uri,
                        MimeType = "application/json",
                        Text = data.ToString(Formatting.None)
                    }
                }
            };
        }

        private static ReadResourceResult TextResource(string uri, string text, string mimeType = "text/plain")
        {
            if (text == null) return null;
            return new ReadResourceResult
            {
                Contents = new List<TextResourceContent>
                {
                    new TextResourceContent
                    {
                        Uri = uri,
                        MimeType = mimeType,
                        Text = text
                    }
                }
            };
        }

        private static int ParseCellCoord(string text)
        {
            if (string.IsNullOrEmpty(text)) return Grid.InvalidCell;
            var parts = text.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                return Grid.XYToCell(x, y);
            if (int.TryParse(text, out int cell))
                return cell;
            return Grid.InvalidCell;
        }
    }
}

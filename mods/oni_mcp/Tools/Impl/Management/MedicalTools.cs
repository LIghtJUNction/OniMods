using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class MedicalTools
    {
        public static McpTool ListPatients()
        {
            return new McpTool
            {
                Name = "medical_patients_list",
                Group = "medical",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "sick_dupes_list", "injured_dupes_list", "patients_list" },
                Tags = new List<string> { "medical", "patients", "sickness", "health", "dupes" },
                Description = "兼容入口：列出需要医疗关注的复制人。新调用请使用 colony_control domain=management kind=medical action=patients。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["includeHealthy"] = new McpToolParameter { Type = "boolean", Description = "是否包含健康复制人，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按复制人名称或疾病 ID 筛选", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认不过滤", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? -1;
                    bool includeHealthy = ToolUtil.GetBool(args, "includeHealthy", false);
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var patients = Components.LiveMinionIdentities.Items
                        .Where(dupe => dupe != null && ToolUtil.GameObjectMatchesWorld(dupe.gameObject, worldId))
                        .Select(PatientInfo)
                        .Where(info => includeHealthy || (bool)info["needsMedicalAttention"])
                        .Where(info => PatientMatches(info, query))
                        .OrderByDescending(info => (bool)info["needsMedicalAttention"])
                        .ThenBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = patients.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["includeHealthy"] = includeHealthy,
                        ["patients"] = patients
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlMedical()
        {
            return new McpTool
            {
                Name = "medical_control",
                Group = "medical",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "medical_care_control", "clinic_control", "patient_control" },
                Tags = new List<string> { "medical", "patients", "clinic", "doctor", "threshold", "assign", "batch" },
                Description = "统一读取和管理医疗状态。action=patients/clinics/doctor_stations/set_threshold/batch_set_threshold/assign_bed。",
                Parameters = RectParams(LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "patients、clinics、doctor_stations、set_threshold、batch_set_threshold、assign_bed，默认 patients", Required = false, EnumValues = new List<string> { "patients", "clinics", "doctor_stations", "set_threshold", "batch_set_threshold", "assign_bed" } },
                    ["includeHealthy"] = new McpToolParameter { Type = "boolean", Description = "action=patients 时是否包含健康复制人，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按复制人、疾病、建筑、prefab、药品等筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回或处理数量，默认 100，最大 500", Required = false },
                    ["thresholdPercent"] = new McpToolParameter { Type = "number", Description = "action=set_threshold/batch_set_threshold/assign_bed 时可设置治疗阈值百分比 0-100", Required = false },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "action=assign_bed 时要分配的复制人 InstanceID", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "action=assign_bed 时要分配的复制人名称", Required = false },
                    ["bedAction"] = new McpToolParameter { Type = "string", Description = "action=assign_bed 时为 assign 或 unassign，默认 assign", Required = false, EnumValues = new List<string> { "assign", "unassign" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写操作必须为 true", Required = false }
                })),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "patients").Trim().ToLowerInvariant();
                    if (action == "patients")
                        return ListPatients().Handler(args);
                    if (action == "clinics")
                        return ListClinics().Handler(args);
                    if (action == "doctor_stations")
                        return ListDoctorStations().Handler(args);
                    if (action == "set_threshold")
                        return SetClinicThreshold().Handler(args);
                    if (action == "batch_set_threshold")
                        return BatchSetClinicThreshold().Handler(args);
                    if (action == "assign_bed")
                        return AssignMedicalBed().Handler(MedicalBedAssignArgs(args));
                    return CallToolResult.Error("action must be patients, clinics, doctor_stations, set_threshold, batch_set_threshold, or assign_bed");
                }
            };
        }

        public static McpTool ListClinics()
        {
            return new McpTool
            {
                Name = "medical_clinics_list",
                Group = "medical",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "medical_beds_list", "clinics_list" },
                Tags = new List<string> { "medical", "clinic", "bed", "threshold", "care" },
                Description = "兼容入口：列出医疗床/诊所及其治疗阈值、分配对象和优先级。新调用请使用 colony_control domain=management kind=medical action=clinics。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var clinics = Components.Clinics.Items
                        .Where(clinic => clinic != null && ToolUtil.GameObjectMatchesWorld(clinic.gameObject, worldId))
                        .Where(clinic => rect == null || CellInRect(Grid.PosToCell(clinic.gameObject), rect, worldId))
                        .OrderBy(clinic => TargetName(clinic.gameObject))
                        .Take(limit)
                        .Select(ClinicInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = clinics.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["clinics"] = clinics
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlClinic()
        {
            return new McpTool
            {
                Name = "medical_clinic_control",
                Group = "medical",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "medical_clinics_control", "medical_bed_control", "clinic_threshold_control" },
                Tags = new List<string> { "medical", "clinic", "bed", "threshold", "care", "batch" },
                Description = "兼容入口：统一读取/设置医疗床或诊所治疗阈值。新调用请使用 colony_control domain=management kind=medical action=clinics/set_threshold/batch_set_threshold。",
                Parameters = RectParams(LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、set、batch，默认 list", Required = false, EnumValues = new List<string> { "list", "set", "batch" } },
                    ["thresholdPercent"] = new McpToolParameter { Type = "number", Description = "治疗阈值百分比 0-100；action=set/batch 时必填", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=batch/list 时按医疗床/诊所名称或 prefabId 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回或处理数量，默认 100，最大 500", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=set/batch 修改阈值时必须为 true", Required = false }
                })),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "list").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListClinics().Handler(args);
                    if (action == "set")
                        return SetClinicThreshold().Handler(args);
                    if (action == "batch")
                        return BatchSetClinicThreshold().Handler(args);
                    return CallToolResult.Error("action must be list, set, or batch");
                }
            };
        }

        public static McpTool ListDoctorStations()
        {
            return new McpTool
            {
                Name = "doctor_stations_list",
                Group = "medical",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "medical_doctor_stations_list", "treatment_stations_list" },
                Tags = new List<string> { "medical", "doctor", "station", "medicine", "treatment" },
                Description = "兼容入口：列出医生站/高级医生站、药品库存、可治疗疾病和当前可治疗患者。新调用请使用 colony_control domain=management kind=medical action=doctor_stations。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、药品或疾病 ID 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var stations = Components.BuildingCompletes.Items
                        .Select(building => building?.GetComponent<DoctorStation>())
                        .Where(station => station != null && ToolUtil.GameObjectMatchesWorld(station.gameObject, worldId))
                        .Where(station => rect == null || CellInRect(Grid.PosToCell(station.gameObject), rect, worldId))
                        .Select(DoctorStationInfo)
                        .Where(info => DoctorStationMatches(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = stations.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["doctorStations"] = stations
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetClinicThreshold()
        {
            return new McpTool
            {
                Name = "medical_clinic_threshold_set",
                Group = "medical",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "medical_bed_threshold_set", "clinic_threshold_set" },
                Tags = new List<string> { "medical", "clinic", "bed", "threshold", "care" },
                Description = "兼容入口：设置医疗床/诊所的治疗阈值百分比。新调用请使用 colony_control domain=management kind=medical action=set_threshold。",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["thresholdPercent"] = new McpToolParameter { Type = "number", Description = "治疗阈值百分比 0-100", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改医疗床阈值，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to change a medical clinic threshold");
                    var clinic = FindClinic(args);
                    if (clinic == null)
                        return CallToolResult.Error("Clinic target not found");
                    float? threshold = ToolUtil.GetFloat(args, "thresholdPercent");
                    if (!threshold.HasValue)
                        return CallToolResult.Error("thresholdPercent is required");

                    SetClinicThresholdValue(clinic, threshold.Value);
                    return CallToolResult.Text(JsonConvert.SerializeObject(ClinicInfo(clinic), McpJsonUtil.Settings));
                }
            };
        }

    }
}

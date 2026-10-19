using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class GameControlTools
    {
        private sealed partial class StepUntilWatcher
        {
            private static readonly FieldInfo ScreenNotificationsField = AccessTools.Field(typeof(NotificationScreen), "notifications");
            private static readonly FieldInfo ManagerNotificationsField = AccessTools.Field(typeof(NotificationManager), "notifications");

            private void ScanNotificationsAndAlarms(
                out string threatAlert,
                out string triggeredAlarm,
                out List<string> activeAlarms,
                out List<string> activeAlerts,
                out List<StepNotificationFact> details,
                out int newCount)
            {
                threatAlert = null;
                triggeredAlarm = null;
                activeAlarms = new List<string>();
                activeAlerts = new List<string>();
                details = new List<StepNotificationFact>();
                newCount = 0;

                // 1. Red Alert
                if (ClusterManager.Instance?.activeWorld?.AlertManager != null &&
                    ClusterManager.Instance.activeWorld.AlertManager.IsRedAlert())
                {
                    threatAlert = "red_alert";
                    activeAlerts.Add("red_alert");
                }

                // 2. Duplicant Health / Breath
                if (Components.LiveMinionIdentities != null)
                {
                    foreach (var dupe in Components.LiveMinionIdentities.Items)
                    {
                        if (dupe == null) continue;
                        float breath = DupeAmountUtil.AmountValueByName(dupe, "Breath", 100f);
                        if (breath < 20f)
                        {
                            string s = $"{dupe.GetProperName()}_suffocating";
                            if (threatAlert == null) threatAlert = s;
                            activeAlerts.Add(s);
                        }

                        float hp = DupeAmountUtil.AmountValueByName(dupe, "HitPoints", 100f);
                        if (hp < 35f)
                        {
                            string s = $"{dupe.GetProperName()}_low_health";
                            if (threatAlert == null) threatAlert = s;
                            activeAlerts.Add(s);
                        }
                    }
                }

                // 3. Scan all notifications
                foreach (var n in GetAllNotifications())
                {
                    if (n == null) continue;
                    string title = n.titleText ?? string.Empty;
                    var notifierGo = n.Notifier != null ? n.Notifier.gameObject : null;
                    bool isLogicAlarm = notifierGo != null && notifierGo.GetComponent<LogicAlarm>() != null;
                    bool isNew = _initialNotificationKeys != null && !_initialNotificationKeys.Contains(GetNotificationKey(n));
                    if (isNew) newCount++;

                    string notifierName = notifierGo != null ? ToolUtil.CleanName(notifierGo.GetProperName()) : (n.NotifierName ?? string.Empty);

                    details.Add(new StepNotificationFact
                    {
                        Title = title,
                        Type = n.Type.ToString(),
                        IsNew = isNew,
                        IsAlarm = isLogicAlarm,
                        Notifier = notifierName
                    });

                    if (isLogicAlarm)
                    {
                        string alarmName = string.IsNullOrEmpty(title) ? "LogicAlarm" : title;
                        activeAlarms.Add(alarmName);
                        if (triggeredAlarm == null) triggeredAlarm = alarmName;
                    }

                    if (n.Type == NotificationType.DuplicantThreatening)
                    {
                        string threatName = string.IsNullOrEmpty(title) ? "DuplicantThreatening" : title;
                        if (threatAlert == null) threatAlert = threatName;
                        activeAlerts.Add(threatName);
                    }
                    else if (n.Type == NotificationType.Bad)
                    {
                        activeAlerts.Add(string.IsNullOrEmpty(title) ? "BadNotification" : title);
                    }
                }
            }

            private static string GetNotificationKey(Notification n) => $"{n.titleText}|{n.Type}|{n.Idx}";

            private static List<Notification> GetAllNotifications()
            {
                var list = new List<Notification>();
                var sn = ScreenNotificationsField?.GetValue(NotificationScreen.Instance) as IEnumerable<Notification>;
                if (sn != null) list.AddRange(sn.Where(x => x != null));
                var mn = ManagerNotificationsField?.GetValue(NotificationManager.Instance) as IEnumerable<Notification>;
                if (mn != null) list.AddRange(mn.Where(x => x != null));
                return list;
            }

        }
    }
}

using System.Collections.Generic;
using HarmonyLib;
using OniMcp.Config;

namespace OniMcp.Patches
{
    /// <summary>Filters native tutorial notifications at insertion and tutorial messages at enqueue.</summary>
    [HarmonyPatch(typeof(NotificationManager), "AddNotification")]
    internal static class TutorialNotificationAutoDismissPatch
    {
        private static bool Prefix(Notification notification)
        {
            if (!OniMcpOptions.Current.AutoDismissTutorialNotifications
                || notification == null || notification.Type != NotificationType.Tutorial)
                return true;

            // Notifier.Add has already associated the notifier, so native Clear removes the pending UI state too.
            notification.Clear();
            return false;
        }
    }

    [HarmonyPatch(typeof(NotificationScreen), "OnNewMessage")]
    internal static class TutorialMessageNotificationPatch
    {
        private static bool Prefix(object data)
        {
            if (!OniMcpOptions.Current.AutoDismissTutorialNotifications || !(data is TutorialMessage tutorialMessage))
                return true;

            Messenger.Instance?.RemoveMessage(tutorialMessage);
            return false;
        }
    }

    [HarmonyPatch(typeof(NotificationScreen), "OnSpawn")]
    internal static class ExistingTutorialMessageNotificationPatch
    {
        private static void Prefix()
        {
            if (!OniMcpOptions.Current.AutoDismissTutorialNotifications || Messenger.Instance == null)
                return;

            var queuedTutorials = new List<TutorialMessage>();
            var messages = Messenger.Instance.Messages;
            for (int index = 0; index < messages.Count; index++)
            {
                if (messages[index] is TutorialMessage tutorialMessage)
                    queuedTutorials.Add(tutorialMessage);
            }

            foreach (TutorialMessage tutorialMessage in queuedTutorials)
                Messenger.Instance.RemoveMessage(tutorialMessage);
        }
    }
}

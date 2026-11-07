using System;
using Jlib.Runtime;
using UnityEngine;

namespace OniMcp.Server
{
    /// <summary>
    /// 主线程桥接器，底层由 Jlib.Runtime.JlibDispatcher 统一托管执行。
    /// </summary>
    public class MainThreadBridge : MonoBehaviour
    {
        private static MainThreadBridge _instance;
        public static MainThreadBridge Instance => _instance ?? (JlibDispatcher.Instance != null ? (_instance = UnityEngine.Object.FindFirstObjectByType<MainThreadBridge>() ?? new GameObject("OniMcp_MainThreadBridge").AddComponent<MainThreadBridge>()) : null);

        private void Awake()
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            JlibDispatcher.EnsureInitialized();
        }

        public static void Enqueue(System.Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            JlibDispatcher.Enqueue(action);
        }

        public static void EnqueueDeferred(System.Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            JlibDispatcher.EnqueueDeferred(action);
        }

        public static T Invoke<T>(Func<T> func, int timeoutMs = 10000)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));
            return JlibDispatcher.Invoke(func, timeoutMs);
        }
    }
}

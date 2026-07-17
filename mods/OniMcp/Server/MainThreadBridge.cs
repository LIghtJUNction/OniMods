using System;
using System.Collections.Generic;
using System.Threading;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Server
{
    /// <summary>
    /// 主线程桥接器。
    /// Unity API 必须在主线程调用，此组件在每帧 Update 中执行队列中的操作。
    /// </summary>
    public class MainThreadBridge : MonoBehaviour
    {
        public static MainThreadBridge Instance { get; private set; }

        private readonly Queue<System.Action> _queueA = new Queue<System.Action>();
        private readonly Queue<System.Action> _queueB = new Queue<System.Action>();
        private readonly object _queueLock = new object();
        private Queue<System.Action> _enqueueQueue;
        private Queue<System.Action> _dequeueQueue;
        private int _mainThreadId;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _enqueueQueue = _queueA;
            _dequeueQueue = _queueB;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            lock (_queueLock)
            {
                if (_enqueueQueue.Count == 0)
                    return;

                var nextDequeueQueue = _enqueueQueue;
                _enqueueQueue = _dequeueQueue;
                _dequeueQueue = nextDequeueQueue;
            }

            while (_dequeueQueue.Count > 0)
            {
                var action = _dequeueQueue.Dequeue();
                ExecuteAction(action);
            }
        }

        private static void ExecuteAction(System.Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                OniMcpLog.Error($"[OniMcp] MainThreadBridge action error: {ex.Message}");
            }
        }

        private bool IsMainThread()
        {
            return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        private void EnqueueInstance(System.Action action, bool allowInline)
        {
            if (allowInline && IsMainThread())
            {
                ExecuteAction(action);
                return;
            }

            lock (_queueLock)
            {
                _enqueueQueue.Enqueue(action);
            }
        }

        /// <summary>
        /// 将操作加入主线程队列
        /// </summary>
        public static void Enqueue(System.Action action)
        {
            if (Instance == null)
            {
                OniMcpLog.Warning("[OniMcp] MainThreadBridge not initialized, executing inline");
                action();
                return;
            }

            Instance.EnqueueInstance(action, allowInline: true);
        }

        /// <summary>
        /// 将操作加入主线程队列，即使调用方已经在主线程上也延后到后续 Update 执行。
        /// </summary>
        public static void EnqueueDeferred(System.Action action)
        {
            if (Instance == null)
            {
                OniMcpLog.Warning("[OniMcp] MainThreadBridge not initialized, executing inline");
                action();
                return;
            }

            Instance.EnqueueInstance(action, allowInline: false);
        }

        public static T Invoke<T>(Func<T> func, int timeoutMs = 10000)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            if (Instance == null)
            {
                OniMcpLog.Warning("[OniMcp] MainThreadBridge not initialized, executing inline");
                return func();
            }

            if (Instance.IsMainThread())
                return func();

            T result = default(T);
            Exception error = null;
            using (var done = new ManualResetEventSlim(false))
            {
                Instance.EnqueueInstance(() =>
                {
                    try
                    {
                        result = func();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                }, allowInline: false);

                if (!done.Wait(timeoutMs))
                    throw new TimeoutException("Timed out waiting for Unity main thread.");
            }

            if (error != null)
                throw error;
            return result;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}

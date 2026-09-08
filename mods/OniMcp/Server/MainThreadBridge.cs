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
        private static MainThreadBridge _instance;
        public static MainThreadBridge Instance => Volatile.Read(ref _instance);

        private readonly Queue<System.Action> _queueA = new Queue<System.Action>();
        private readonly Queue<System.Action> _queueB = new Queue<System.Action>();
        private readonly object _queueLock = new object();
        private readonly HashSet<System.Action> _pendingCancellations = new HashSet<System.Action>();
        private Queue<System.Action> _enqueueQueue;
        private Queue<System.Action> _dequeueQueue;
        private int _mainThreadId;
        private bool _destroyed;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            _enqueueQueue = _queueA;
            _dequeueQueue = _queueB;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            DontDestroyOnLoad(gameObject);
            Volatile.Write(ref _instance, this);
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

        private void EnqueueInstance(System.Action action, bool allowInline, System.Action cancel = null)
        {
            if (allowInline && IsMainThread())
            {
                ExecuteAction(action);
                return;
            }

            lock (_queueLock)
            {
                if (_destroyed)
                    throw new InvalidOperationException("Unity main thread bridge has been destroyed.");
                if (cancel != null)
                {
                    var pendingAction = action;
                    action = () =>
                    {
                        try { pendingAction(); }
                        finally
                        {
                            lock (_queueLock)
                                _pendingCancellations.Remove(cancel);
                        }
                    };
                    _pendingCancellations.Add(cancel);
                }
                _enqueueQueue.Enqueue(action);
            }
        }

        /// <summary>
        /// 将操作加入主线程队列
        /// </summary>
        public static void Enqueue(System.Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            RequireInstance().EnqueueInstance(action, allowInline: true);
        }

        /// <summary>
        /// 将操作加入主线程队列，即使调用方已经在主线程上也延后到后续 Update 执行。
        /// </summary>
        public static void EnqueueDeferred(System.Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            RequireInstance().EnqueueInstance(action, allowInline: false);
        }

        public static T Invoke<T>(Func<T> func, int timeoutMs = 10000)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            if (timeoutMs < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            var instance = RequireInstance();
            if (instance.IsMainThread())
                return func();

            var invocation = new MainThreadInvocation<T>(func);
            instance.EnqueueInstance(invocation.Execute, allowInline: false, cancel: invocation.Cancel);
            return invocation.Wait(timeoutMs);
        }

        private static MainThreadBridge RequireInstance()
        {
            var instance = Instance;
            // Unity's overloaded null comparison is not safe to use on HTTP worker threads.
            if (ReferenceEquals(instance, null))
                throw new InvalidOperationException("Unity main thread bridge is not initialized.");
            return instance;
        }

        private void OnDestroy()
        {
            Interlocked.CompareExchange(ref _instance, null, this);
            System.Action[] cancellations;
            lock (_queueLock)
            {
                _destroyed = true;
                cancellations = new System.Action[_pendingCancellations.Count];
                _pendingCancellations.CopyTo(cancellations);
                _pendingCancellations.Clear();
                _queueA.Clear();
                _queueB.Clear();
            }
            foreach (var cancel in cancellations)
                cancel();
        }
    }
}

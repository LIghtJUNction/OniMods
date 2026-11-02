using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CycleTrim.Core;
using CycleTrim.Patches;

namespace UnityEngine
{
    internal class Object
    {
        internal static bool FailPersistence { get; set; }
        internal static int Destroyed { get; private set; }
        public static void DontDestroyOnLoad(Object value)
        {
            if (FailPersistence) throw new InvalidOperationException("synthetic persistence failure");
        }
        public static void Destroy(Object value)
        {
            Destroyed++;
            var gameObject = value as GameObject;
            if (gameObject != null) gameObject.DestroyComponents();
            else value.GetType().GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(value, null);
        }
    }
    internal class MonoBehaviour : Object { }
    internal sealed class GameObject : Object
    {
        internal static int Created;
        internal static GameObject LastCreated;
        private readonly List<object> components = new List<object>();
        internal GameObject(string name) { Created++; LastCreated = this; }
        internal T AddComponent<T>() where T : new()
        {
            var component = new T();
            components.Add(component);
            return component;
        }
        internal void DestroyComponents()
        {
            foreach (var component in components)
                component.GetType().GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(component, null);
        }
    }
    internal static class Time { internal static float realtimeSinceStartup { get; set; } }
    internal static class Debug
    {
        internal static void Log(string value) { }
        internal static void LogWarning(string value) { }
    }
}

internal sealed class Game
{
    internal static Game Instance { get; set; }
    internal bool baseAlreadyCreated { get; set; }
    internal bool IsLoading() { return false; }
}

internal sealed class SpeedControlScreen
{
    internal static SpeedControlScreen Instance { get; set; }
    internal bool IsPaused { get; set; }
}

namespace CycleTrim.Fetch.Tests
{
    // Setup/inspection only: measured loops execute the real controller getter
    // and counters. No file polling or reflection occurs in the timed section.
    internal static class FetchTestControl
    {
        private static readonly FieldInfo Active = typeof(FetchLayoutExperiment)
            .GetField("active", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo Mode = typeof(FetchLayoutControl)
            .GetField("compact", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo Baseline = typeof(FetchLayoutControl)
            .GetField("baselineCalls", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CompactCount = typeof(FetchLayoutControl)
            .GetField("compactCalls", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FetchLayoutControl session;

        internal static bool IsActive { get { return Active.GetValue(null) != null; } }

        internal static bool Compact
        {
            get { return FetchLayoutExperiment.Compact; }
            set
            {
                if (session == null)
                {
                    // Constructor validates a path but does not read/write it.
                    session = new FetchLayoutControl(Path.Combine(Path.GetTempPath(),
                        "cycletrim-fetch-unused-test-control"), Environment.ProcessId);
                    Active.SetValue(null, session);
                }
                Mode.SetValue(session, value);
            }
        }

        internal static long BaselineCalls { get { return session == null ? 0L : (long)Baseline.GetValue(session); } }
        internal static long CompactCalls { get { return session == null ? 0L : (long)CompactCount.GetValue(session); } }

        internal static void Reset()
        {
            Active.SetValue(null, null);
            session = null;
        }
    }
}

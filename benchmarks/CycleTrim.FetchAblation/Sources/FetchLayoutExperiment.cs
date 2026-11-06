using System;
using System.Diagnostics;
using CycleTrim.Core;
using UnityEngine;

namespace CycleTrim.Patches
{
    // The controller exists only for an explicit lab launch. Normal launches
    // keep the established collector and perform no polling or counter writes.
    internal sealed class FetchLayoutExperiment : MonoBehaviour
    {
        private static FetchLayoutExperiment instance;
        private static volatile FetchLayoutControl active;
        private FetchLayoutControl control;
        private float nextRead;
        private string lastError;

        internal static bool Compact
        {
            get
            {
                var session = active;
                return session != null && session.Compact;
            }
        }

        internal static void StartIfRequested()
        {
            StartIfRequested(Environment.GetCommandLineArgs());
        }

        internal static void StartIfRequested(string[] arguments)
        {
            if (instance != null)
                return;
            GameObject owner = null;
            FetchLayoutExperiment controller = null;
            try
            {
                var path = FetchLayoutControl.FindControlPath(arguments);
                if (path == null)
                    return;
                FetchLayoutControl session;
                using (var process = Process.GetCurrentProcess())
                    session = new FetchLayoutControl(path, process.Id);
                owner = new GameObject("CycleTrim.FetchLayoutExperiment");
                controller = owner.AddComponent<FetchLayoutExperiment>();
                controller.control = session;
                UnityEngine.Object.DontDestroyOnLoad(owner);
                instance = controller;
                active = session;
                UnityEngine.Debug.Log("[CycleTrim][LayoutLab] baseline; explicit control active");
            }
            catch (Exception error)
            {
                if (ReferenceEquals(instance, controller))
                {
                    active = null;
                    instance = null;
                }
                if (owner != null)
                    UnityEngine.Object.Destroy(owner);
                UnityEngine.Debug.LogWarning("[CycleTrim][LayoutLab] disabled: " + error.Message);
            }
        }

        internal static void RecordUpdate(bool compact)
        {
            var session = active;
            if (session != null)
                session.RecordUpdate(compact);
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < nextRead || control == null)
                return;
            nextRead = Time.realtimeSinceStartup + 0.25f;
            try
            {
                var game = Game.Instance;
                var speed = SpeedControlScreen.Instance;
                if (control.Poll(game != null && game.baseAlreadyCreated && !game.IsLoading()
                    && speed != null && speed.IsPaused))
                    lastError = null;
            }
            catch (Exception error)
            {
                if (lastError != error.Message)
                    UnityEngine.Debug.LogWarning("[CycleTrim][LayoutLab] " + error.Message);
                lastError = error.Message;
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(instance, this))
            {
                active = null;
                instance = null;
            }
        }
    }
}

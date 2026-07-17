using HarmonyLib;
using KMod;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Support;
using OniMcp.Tools;
using PeterHan.PLib.Core;
using PeterHan.PLib.Database;
using PeterHan.PLib.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OniMcp
{
    /// <summary>
    /// Mod 入口类。游戏加载时自动实例化。
    /// </summary>
    public class ModInfo : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);

            var modAssembly = assembly ?? typeof(ModInfo).Assembly;

            OniMcpPaths.Initialize(path, modAssembly);
            OniMcpOptions.Reload();
            PUtil.InitLibrary();
            Localization.RegisterForTranslation(typeof(STRINGS));

            try
            {
                var options = new POptions();
                options.RegisterOptions(this, typeof(OniMcpOptions));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OniMcp] POptions registration skipped: {e}");
            }

            // 注册 Harmony Patch
            harmony.PatchAll();
            InputSafetyPatchVerifier.EnsureInstalled(harmony, "OnLoad");
            AutoDisinfectPolicy.EnsureInstalled(harmony, "OnLoad");
            OniMcpLog.Debug($"[OniMcp] Loaded assembly {modAssembly.GetName().Version} from {modAssembly.Location}");

            // 初始化 Tool 注册表
            OniToolRegistry.Initialize();

            // 尽早启动 MCP 服务器和主线程桥接器，使主菜单阶段即可连接
            var bridgeObj = new GameObject("OniMcp_MainThreadBridge");
            bridgeObj.AddComponent<MainThreadBridge>();
            GameRestartCoordinator.EnsureCreated();
            var serverObj = new GameObject("OniMcp_HttpServer");
            serverObj.AddComponent<McpHttpServer>();

            OniMcpLog.Debug("[OniMcp] Mod loaded. MCP Server is starting...");
        }
    }

    /// <summary>
    /// 在数据库初始化后创建框选编辑工具运行时实例
    /// Db.Initialize() 是 ONI mod 最标准的游戏内切入点
    /// </summary>
}

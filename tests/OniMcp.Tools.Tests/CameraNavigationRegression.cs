using System;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;
using UnityEngine;

internal static class CameraNavigationRegression
{
    internal static void Run()
    {
        Camera.main = new Camera();
        CameraController.Instance = new CameraController();
        ClusterManager.Instance = new ClusterManager();
        SpeedControlScreen.Instance = new SpeedControlScreen();
        SpeedControlScreen.Instance.Pause(false);
        var native = CameraController.Instance;
        native.SnapTo(new Vector3(133f, 208f, -100f), 10f);
        var tool = NavigationControlTools.ControlNavigation();
        Check(tool.Parameters["action"].EnumValues.Contains("set_zoom"), "public schema must discover set_zoom");

        Call(tool, "{action:'set_zoom',zoom:23}");
        native.AdvanceFrame();
        Check(Camera.main.orthographicSize == 23f && native.transform.Position.x == 133f
            && native.transform.Position.y == 208f, "zoom-only action must persist without moving the center");

        Call(tool, "{action:'set_view',x:134,y:209,zoom:16,snap:false}");
        native.AdvanceFrame();
        Check(Camera.main.orthographicSize == 16f && native.targetOrthographicSize == 16f
            && native.transform.Position.x == 134f, "smooth set_view must retain its requested native zoom target");

        Call(tool, "{action:'move',mode:'pan',dx:2,dy:1,zoom:20,snap:false,duration:0.5}");
        native.AdvanceFrame();
        Check(Camera.main.orthographicSize == 20f && native.transform.Position.x == 136f
            && native.Speed == 2f, "smooth pan must preserve zoom across a native frame");
        Call(tool, "{action:'move',mode:'pan',zoom:25,snap:true}");
        Check(Camera.main.orthographicSize == 25f && native.transform.Position.x == 136f,
            "pan with only zoom must not require dummy coordinate offsets");
        Call(tool, "{action:'set_view',x:135,y:208}");
        Check(Camera.main.orthographicSize == 25f, "position-only set_view must preserve zoom");

        foreach (string args in new[] { "{action:'set_zoom'}", "{action:'set_zoom',zoom:0}",
            "{action:'set_zoom',zoom:-1}", "{action:'set_zoom',zoom:15,duration:0}" })
        {
            Check(tool.Handler(JObject.Parse(args)).IsError, "invalid zoom must be rejected");
            Check(Camera.main.orthographicSize == 25f, "invalid zoom changed the camera");
        }
        Check(tool.Handler(new JObject { ["action"] = "set_zoom", ["zoom"] = float.NaN }).IsError,
            "NaN zoom must be rejected");
        Check(tool.Handler(JObject.Parse("{action:'set_view',x:302,y:208,zoom:15,snap:false}")).IsError
            && native.targetOrthographicSize == 25f,
            "camera coordinates must not bypass discovered-world switching through native SetTargetPos");

        float aspect = 1228f / 1516f;
        string mode = CameraFramingPolicy.ResolveMode(null);
        Check(mode == "fit", "automatic framing must fit the rectangle instead of selecting a cropped close-up");
        float fit = CameraFramingPolicy.ForBounds(27, 14, aspect, 1.5f, mode, 4f, 80f);
        Check(fit * 2f * aspect >= 30f - 0.001f && fit * 2f >= 17f,
            "automatic fit must cover both dimensions and padding on a portrait screen");
        float detail = CameraFramingPolicy.ForBounds(27, 14, aspect, 1.5f, "detail", 4f, 80f);
        Check(detail < fit, "explicit close-up mode must remain available");
        float smallFit = CameraFramingPolicy.ForBounds(21, 15, aspect, 1.5f, mode, 4f, 80f);
        Check(smallFit * 2f * aspect >= 24f - 0.001f && smallFit * 2f >= 18f,
            "small rectangles must also fit; the old implicit detail mode cropped them");
        native.UnpauseOnTarget = true;
        Call(tool, "{action:'set_zoom',zoom:24,snap:false}");
        native.AdvanceFrame();
        Check(SpeedControlScreen.Instance.IsPaused && SpeedControlScreen.Instance.PauseCount == 1,
            "native cinematic unpause must not change gameplay pause state");
        Console.WriteLine("PASS: public camera zoom, persistent smooth movement and full-bounds framing");
    }

    private static void Call(McpTool tool, string args)
    {
        var result = tool.Handler(JObject.Parse(args));
        Check(!result.IsError, "camera request failed: " + args);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

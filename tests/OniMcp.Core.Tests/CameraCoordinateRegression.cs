using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class CameraCoordinateRegression
{
    internal static void Run()
    {
        foreach (var args in new[]
        {
            JObject.Parse("{action:'set_view',x:133,y:208,zoom:16,snap:true}"),
            JObject.Parse("{domain:'camera',action:'move',mode:'pan',dx:0,dy:0,zoom:20}"),
            JObject.Parse("{domain:'view',action:'move',mode:'jump',x:133,y:208}"),
            JObject.Parse("{action:'focus_cell',x:133,y:208}"),
            JObject.Parse("{action:'coordinate_screenshot',x1:120,y1:200,x2:146,y2:215}")
        })
        {
            ExpectDispatch("navigation_control", args, true);
            ExpectDispatch("legacy_navigation_control", args, true);
        }

        foreach (var args in new[]
        {
            JObject.Parse("{domain:'pointer',action:'set_view',x:1,y:2}"),
            JObject.Parse("{action:'build_area',x:1,y:2}"),
            JObject.Parse("{action:'get_view',x:1}"),
            JObject.Parse("{action:'set_zoom',zoom:20,x:1}"),
            JObject.Parse("{action:'move',mode:'pan',x:1,y:2}"),
            JObject.Parse("{action:'set_view',x:1,y:2,target:{x:3}}"),
            JObject.Parse("{action:'set_view',x:1,y:2,anchors:[{x:3,y:4}]}"),
            JObject.Parse("{action:'set_view',x:{value:1},y:2}"),
            JObject.Parse("{action:'coordinate_screenshot',cells:[4]}")
        })
            ExpectDispatch("navigation_control", args, false);

        foreach (string name in new[] { "building_control", "orders_control", "game_control", "server_control", "benchmark" })
            ExpectDispatch(name, JObject.Parse("{action:'set_view',x:1,y:2}"), false);
        ExpectDispatch("server_control", JObject.Parse("{domain:'batch',action:'call_many',calls:[{name:'navigation_control',arguments:{action:'set_view',x:1,y:2}}]}"), false);

        var navigation = OniToolRegistry.GetToolInfos().Single(tool => tool.Name == "navigation_control");
        if (!navigation.InputSchema.Properties.ContainsKey("x"))
            throw new InvalidOperationException("public navigation schema hid its supported camera coordinate");
        if (CameraCoordinatePolicy.ExposesParameter("navigation_control", "anchors")
            || CameraCoordinatePolicy.ExposesParameter("building_control", "x"))
            throw new InvalidOperationException("camera schema exception widened gameplay coordinate input");
    }

    private static void ExpectDispatch(string name, JObject args, bool allowed)
    {
        args["task"] = "verify camera-only coordinate routing";
        int before = GameStubs.HandlerCalls;
        var result = OniToolRegistry.CallTool(name, args);
        if (result.IsError == allowed || GameStubs.HandlerCalls != before + (allowed ? 1 : 0))
            throw new InvalidOperationException("unexpected coordinate routing for " + name + ": " + args);
    }
}

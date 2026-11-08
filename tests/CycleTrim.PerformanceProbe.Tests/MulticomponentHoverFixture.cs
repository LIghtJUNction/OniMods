using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Emit;
using UnityEngine;

// Host boundaries for the linked Jlib formatter and hover-card implementation.
// Unit formatting is deterministic; these tests do not validate the game's localization.
public static class GameUtil
{
    public static string GetFormattedMass(float mass)
    {
        return mass.ToString("0.###", CultureInfo.InvariantCulture) + " kg";
    }

    public static string GetFormattedTemperature(float temperature)
    {
        return temperature.ToString("0.###", CultureInfo.InvariantCulture) + " K";
    }
}

public sealed class SelectToolHoverTextCard
{
    public void UpdateHoverElements() { }
}

public sealed class TextStyleSetting { }

public sealed class HoverTextDrawer
{
    private Vector2 currentPos = new Vector2(10f, 20f);
    private Vector2 rootPos = new Vector2(10f, 20f);
    private bool firstShadowBar = true;
    private float maxShadowX;
    public readonly List<string> Text = new List<string>();
    public Vector2 Root { get { return rootPos; } }
    public Vector2 Current { get { return currentPos; } }

    public void DrawText(string text, TextStyleSetting style)
    {
        Text.Add(text);
        currentPos.y -= text.Split('\n').Length;
        maxShadowX = Math.Max(maxShadowX, currentPos.x + text.Length);
    }

    public void BeginShadowBar()
    {
        if (firstShadowBar) maxShadowX = currentPos.x;
        firstShadowBar = false;
    }

    public void EndShadowBar() { }
}

public static class KInputManager
{
    public static Vector3 GetMousePos() { return new Vector3(); }
}

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3 { public float x, y, z; }

    public sealed class Camera
    {
        public static readonly Camera main = new Camera();
        public Vector3 ScreenToWorldPoint(Vector3 position) { return position; }
    }

    public static class Time { public static int frameCount; }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyTranspiler : Attribute { }

    public sealed class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public readonly List<Label> labels = new List<Label>();
        public CodeInstruction(OpCode opcode, object operand = null)
        {
            this.opcode = opcode;
            this.operand = operand;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycleTrim.Patches;
using HarmonyLib;
using UnityEngine;

// Synthetic resource-panel objects. The production Prefix is installed with
// Harmony; no Unity assemblies, game implementation or rendering are executed.
public struct Tag
{
    internal readonly int Id;
    internal Tag(int id) { Id = id; }
}

public sealed class Pickupable : KMonoBehaviour { }
public class KMonoBehaviour : Component { }

public sealed class KAnimControllerBase : UnityEngine.Object
{
    private Color colour = Color.black;
    internal Action<Color> BeforeColour;
    internal int Writes;
    public Color HighlightColour
    {
        get { return colour; }
        set { BeforeColour?.Invoke(value); colour = value; Writes++; }
    }
}

public sealed class ResourceEntry
{
    public Tag Resource = new Tag(1);
    public Color HighlightColor = new Color(1f, 0.5f, 0.25f, 1f);
    public HoverBackground Background = new HoverBackground();
    public Color BackgroundHoverColor = new Color(0.25f, 0.5f, 1f, 1f);
    internal int VanillaCalls;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Hover(bool is_hovering) { VanillaCalls++; }
}

public sealed class ResourceCategoryHeader
{
    public Tag ResourceCategoryTag = new Tag(2);
    public Color highlightColour = new Color(0.25f, 1f, 0.5f, 1f);
    public HoverBackground Background = new HoverBackground();
    public Color BackgroundHoverColor = new Color(0.25f, 0.5f, 1f, 1f);
    internal int VanillaCalls;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Hover(bool is_hovering) { VanillaCalls++; }
}

public sealed class HoverBackground { public Color color { get; set; } }
public sealed class HoverWorld { public HoverInventory worldInventory = new HoverInventory(); }
public sealed partial class ClusterManager { public HoverWorld activeWorld = new HoverWorld(); }

public sealed class HoverInventory
{
    internal readonly Dictionary<int, ICollection<Pickupable>> Items = new Dictionary<int, ICollection<Pickupable>>();
    public ICollection<Pickupable> GetPickupables(Tag tag)
    {
        Items.TryGetValue(tag.Id, out var items);
        return items;
    }
}

public static partial class OverlayModes
{
    public sealed class Mode { public void UpdateHighlightTypeOverlay() { } }
}

namespace UnityEngine
{
    public class Component : Object
    {
        internal object Controller;
        internal Action BeforeLookup;
        internal int Lookups;
        public T GetComponent<T>() where T : class
        {
            Lookups++;
            BeforeLookup?.Invoke();
            return Controller as T;
        }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a)
        { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black { get { return new Color(0f, 0f, 0f, 1f); } }
    }
}

namespace CycleTrim.Optimizations.Tests
{
    internal static class PickupHighlightFixture
    {
        internal static void Install()
        {
            new Harmony("CycleTrim.Tests.PickupHighlight")
                .CreateClassProcessor(typeof(PickupHighlightOptimizationPatch)).Patch();
        }

        internal static HoverInventory Reset()
        {
            ClusterManager.Instance = new ClusterManager();
            return ClusterManager.Instance.activeWorld.worldInventory;
        }

        internal static List<Pickupable> Items(int count)
        {
            var result = new List<Pickupable>(count);
            for (int i = 0; i < count; i++)
                result.Add(new Pickupable { Controller = new KAnimControllerBase() });
            return result;
        }

        internal static bool Same(Color left, Color right)
        {
            return left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a;
        }

        internal static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Pickup highlight: " + description);
        }
    }
}

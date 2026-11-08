using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    [HarmonyPatch]
    internal static class PickupHighlightOptimizationPatch
    {
        private sealed class ControllerReference { internal KAnimControllerBase Controller; }
        private sealed class HoverState { internal readonly List<KAnimControllerBase> Highlighted = new List<KAnimControllerBase>(); internal bool UseVanilla; }
        private sealed class VisitContext
        {
            internal Tag Tag;
            internal Color Colour;
            internal readonly HashSet<KAnimControllerBase> Seen = new HashSet<KAnimControllerBase>();
            internal readonly List<KAnimControllerBase> Highlighted = new List<KAnimControllerBase>();
        }

        private static readonly ConditionalWeakTable<Pickupable, ControllerReference> Controllers = new ConditionalWeakTable<Pickupable, ControllerReference>();
        private static readonly ConditionalWeakTable<object, HoverState> HoverStates = new ConditionalWeakTable<object, HoverState>();
        private static readonly FieldInfo HeaderTag = AccessTools.Field(typeof(ResourceCategoryHeader), "ResourceCategoryTag");
        private static readonly FieldInfo HeaderColour = AccessTools.Field(typeof(ResourceCategoryHeader), "highlightColour");
        private static readonly FieldInfo EntryTag = AccessTools.Field(typeof(ResourceEntry), "Resource");
        private static readonly FieldInfo EntryColour = AccessTools.Field(typeof(ResourceEntry), "HighlightColor");
        private static readonly FieldInfo HeaderBackground = AccessTools.Field(typeof(ResourceCategoryHeader), "Background");
        private static readonly FieldInfo EntryBackground = AccessTools.Field(typeof(ResourceEntry), "Background");
        private static readonly FieldInfo HeaderBackgroundHover = AccessTools.Field(typeof(ResourceCategoryHeader), "BackgroundHoverColor");
        private static readonly FieldInfo EntryBackgroundHover = AccessTools.Field(typeof(ResourceEntry), "BackgroundHoverColor");
        private static readonly PropertyInfo HeaderBackgroundColor = GetBackgroundColorProperty(HeaderBackground);
        private static readonly PropertyInfo EntryBackgroundColor = GetBackgroundColorProperty(EntryBackground);

        private static PropertyInfo GetBackgroundColorProperty(FieldInfo field)
        {
            return field == null ? null : AccessTools.Property(field.FieldType, "color");
        }

        private static bool Prepare()
        {
            return HeaderTag != null && HeaderColour != null && EntryTag != null && EntryColour != null
                && HeaderBackground != null && EntryBackground != null
                && HeaderBackgroundHover != null && EntryBackgroundHover != null
                && AccessTools.DeclaredMethod(typeof(ResourceCategoryHeader), "Hover", new[] { typeof(bool) }) != null
                && AccessTools.DeclaredMethod(typeof(ResourceEntry), "Hover", new[] { typeof(bool) }) != null;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(ResourceCategoryHeader), "Hover", new[] { typeof(bool) });
            yield return AccessTools.DeclaredMethod(typeof(ResourceEntry), "Hover", new[] { typeof(bool) });
        }

        private static bool Prefix(object __instance, bool is_hovering)
        {
            if (__instance == null)
                return true;

            HoverState state;
            if (!HoverStates.TryGetValue(__instance, out state))
            {
                state = new HoverState();
                HoverStates.Add(__instance, state);
            }

            // If the previous enter fell back to vanilla, its matching exit
            // must also run vanilla so it can clear every object it touched.
            if (state.UseVanilla)
            {
                if (!is_hovering)
                    state.UseVanilla = false;
                return true;
            }

            if (ClusterManager.Instance == null || ClusterManager.Instance.activeWorld == null
                || ClusterManager.Instance.activeWorld.worldInventory == null)
            {
                state.UseVanilla = true;
                return true;
            }

            bool header = __instance is ResourceCategoryHeader;
            FieldInfo tagField = header ? HeaderTag : EntryTag;
            FieldInfo colourField = header ? HeaderColour : EntryColour;
            FieldInfo backgroundField = header ? HeaderBackground : EntryBackground;
            FieldInfo backgroundHoverField = header ? HeaderBackgroundHover : EntryBackgroundHover;
            PropertyInfo backgroundColorProperty = header ? HeaderBackgroundColor : EntryBackgroundColor;
            if (tagField == null || colourField == null || backgroundField == null
                || backgroundHoverField == null || backgroundColorProperty == null)
                return true;

            var background = backgroundField.GetValue(__instance);
            if (background != null)
                backgroundColorProperty.SetValue(background, is_hovering
                    ? (Color)backgroundHoverField.GetValue(__instance)
                    : new Color(0f, 0f, 0f, 0f), null);

            if (!is_hovering)
            {
                for (int i = 0; i < state.Highlighted.Count; i++)
                {
                    var controller = state.Highlighted[i];
                    if (controller != null) controller.HighlightColour = Color.black;
                }
                state.Highlighted.Clear();
                return false;
            }

            for (int i = 0; i < state.Highlighted.Count; i++)
            {
                var previous = state.Highlighted[i];
                if (previous != null) previous.HighlightColour = Color.black;
            }
            state.Highlighted.Clear();

            var context = new VisitContext
            {
                Tag = (Tag)tagField.GetValue(__instance),
                Colour = (Color)colourField.GetValue(__instance)
            };
            try
            {
                // Preserve vanilla's all-world semantics: the resource panel
                // intentionally highlights matching items even when the camera
                // is panned later. Cache component resolution, not visibility.
                ICollection<Pickupable> pickupables =
                    ClusterManager.Instance.activeWorld.worldInventory.GetPickupables(context.Tag);
                if (pickupables != null)
                {
                    foreach (Pickupable pickupable in pickupables)
                    {
                        if (pickupable == null)
                            continue;
                        var controller = GetController(pickupable);
                        if (controller == null || !context.Seen.Add(controller))
                            continue;
                        controller.HighlightColour = context.Colour;
                        context.Highlighted.Add(controller);
                    }
                }
            }
            catch (Exception error)
            {
                // Roll back a partially completed pass before vanilla takes
                // over; vanilla must also own the matching pointer-exit event.
                for (int i = 0; i < context.Highlighted.Count; i++)
                {
                    var controller = context.Highlighted[i];
                    if (controller != null)
                        controller.HighlightColour = Color.black;
                }
                state.UseVanilla = true;
                Console.WriteLine("[CycleTrim] Pickup hover cache failed; using vanilla: " + error.Message);
                return true;
            }
            state.Highlighted.AddRange(context.Highlighted);
            return false;
        }

        private static KAnimControllerBase GetController(Pickupable pickupable)
        {
            ControllerReference cached;
            if (Controllers.TryGetValue(pickupable, out cached) && cached.Controller != null)
                return cached.Controller;
            var controller = pickupable.GetComponent<KAnimControllerBase>();
            if (cached == null)
            {
                cached = new ControllerReference { Controller = controller };
                Controllers.Add(pickupable, cached);
            }
            else cached.Controller = controller;
            return controller;
        }
    }

    [HarmonyPatch]
    internal static class OverlayHighlightControllerLookupPatch
    {
        private sealed class ControllerReference { internal KBatchedAnimController Controller; }
        private static readonly ConditionalWeakTable<KMonoBehaviour, ControllerReference> Cache =
            new ConditionalWeakTable<KMonoBehaviour, ControllerReference>();
        private static readonly MethodInfo Lookup = AccessTools.Method(typeof(Component), "GetComponent", Type.EmptyTypes);
        private static readonly MethodInfo Target = AccessTools.Method(typeof(OverlayModes.Mode), "UpdateHighlightTypeOverlay");

        private static bool Prepare()
        {
            // Harmony cannot create a detour for an open generic method. Leave
            // the original overlay path active instead of aborting mod startup.
            return Target != null && !Target.ContainsGenericParameters && Target.ReturnType == typeof(void)
                && Lookup != null && Lookup.IsGenericMethodDefinition;
        }

        private static MethodBase TargetMethod() { return Target; }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var lookup = Lookup.MakeGenericMethod(typeof(KBatchedAnimController));
            var replacement = AccessTools.Method(typeof(OverlayHighlightControllerLookupPatch),
                nameof(GetController), new[] { typeof(KMonoBehaviour) });
            int match = -1, count = 0;
            for (int i = 0; i < code.Count; i++)
                if (Equals(code[i].operand, lookup)) { match = i; count++; }
            if (count != 1 || code[match].opcode != OpCodes.Callvirt || replacement == null)
                return code;
            var call = new CodeInstruction(code[match]);
            call.opcode = OpCodes.Call;
            call.operand = replacement;
            code[match] = call;
            return code;
        }

        private static KBatchedAnimController GetController(KMonoBehaviour target)
        {
            if (target == null) return null;
            ControllerReference cached;
            if (Cache.TryGetValue(target, out cached) && cached.Controller != null)
                return cached.Controller;
            var controller = target.GetComponent<KBatchedAnimController>();
            if (cached == null)
            {
                cached = new ControllerReference { Controller = controller };
                Cache.Add(target, cached);
            }
            else cached.Controller = controller;
            return controller;
        }
    }
}

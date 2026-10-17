using System;
using System.Collections.Generic;
using System.Linq;

// Only component lookup and game data are substituted. The material reader is
// compiled from production source; these tests are not Unity/ONI acceptance.
namespace UnityEngine
{
    internal sealed partial class GameObject
    {
        private readonly Dictionary<Type, object> components = new Dictionary<Type, object>();

        internal GameObject Add<T>(T component) where T : class
        {
            components[typeof(T)] = component;
            if (component is KMonoBehaviour behaviour)
                behaviour.gameObject = this;
            return this;
        }

        internal T GetComponent<T>() where T : class
        {
            return components.Values.OfType<T>().FirstOrDefault();
        }
    }
}

internal partial struct Tag
{
    internal Tag(string name) { Name = name; }
    internal string Name { get; }
    internal bool IsValid => !string.IsNullOrEmpty(Name);
    internal static Tag Invalid => new Tag(null);
}

internal sealed class Constructable
{
    // Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636:
    // Constructable.SelectedElementsTags is an ordered IList<Tag>.
    internal IList<Tag> SelectedElementsTags { get; set; }
}

internal sealed class PrimaryElement
{
    internal SimHashes ElementID { get; set; }
    internal float Mass { get; set; }
}

internal sealed class Reconstructable
{
    // At the same source pin, RequestReconstruct writes the future material.
    // It must not override the completed building's current PrimaryElement.
    internal Tag PrimarySelectedElementTag { get; set; }
}

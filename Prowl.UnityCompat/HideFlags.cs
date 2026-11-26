// Parallel re-export of Prowl.Runtime.HideFlags with the same names and numeric values.

namespace UnityEngine;

/// <summary>Mirrors <see cref="Prowl.Runtime.HideFlags"/>. Use the extension methods below to convert.</summary>
[System.Flags]
public enum HideFlags
{
    None = 0,
    Hide = 1 << 0,
    NotEditable = 1 << 1,
    DontSave = 1 << 2,
    HideAndDontSave = 1 << 3,
    NoGizmos = 1 << 4
}

/// <summary>Conversions between the Unity-compatible flags and Prowl's own enum.</summary>
public static class HideFlagsExtensions
{
    public static Prowl.Runtime.HideFlags ToProwl(this HideFlags flags) => (Prowl.Runtime.HideFlags)flags;
    public static HideFlags ToUnity(this Prowl.Runtime.HideFlags flags) => (HideFlags)flags;
}

// Unity-style [SerializeField], deriving from Echo's attribute so reflection-based serialization sees it.

namespace UnityEngine;

/// <summary>
/// Marks a field for serialization. Derives from <see cref="Prowl.Echo.SerializeFieldAttribute"/> so Echo's
/// reflection-based field scan recognises it.
/// <para/>
/// Echo's source generator matches the exact base attribute type, so generated serializers need the base
/// <c>[Prowl.Echo.SerializeField]</c> attribute rather than this derived one.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class SerializeFieldAttribute : Prowl.Echo.SerializeFieldAttribute
{
}

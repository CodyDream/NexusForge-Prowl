// Unity-style ScriptableObject mapped onto Prowl.Runtime.Asset.

using Prowl.Runtime;

namespace UnityEngine;

/// <summary>
/// Unity-style scriptable object. A runtime <see cref="Asset"/> created with <c>new</c>, so it is never
/// registered in the asset database and stays owned by its caller.
/// </summary>
public abstract class ScriptableObject : Asset
{
    protected ScriptableObject() { }
    protected ScriptableObject(string name) : base(name) { }

    /// <summary>Creates a new runtime instance of <typeparamref name="T"/>.</summary>
    public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
}

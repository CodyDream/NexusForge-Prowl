// Minimal UnityEngine.Object compatibility surface, forwarding to Prowl.Runtime.EngineObject.

using Prowl.Runtime;

namespace UnityEngine;

/// <summary>
/// Unity-style base object. It is a thin wrapper over a <see cref="EngineObject"/>; when no engine object is
/// attached it behaves as a plain named object. Engine objects are always checked with <c>IsValid()</c> rather
/// than compared with null, because a destroyed engine object is still a live reference.
/// </summary>
public class Object
{
    private string _name = string.Empty;

    public Object() { }

    protected Object(EngineObject target) => Target = target;

    /// <summary>The wrapped Prowl engine object, when this instance wraps one.</summary>
    public EngineObject? Target { get; internal set; }

    /// <summary>Unity-style name, mapped onto <see cref="EngineObject.Name"/> when a target is present.</summary>
    public string Name
    {
        get => Target.IsValid() ? Target!.Name : _name;
        set
        {
            _name = value;
            if (Target.IsValid()) Target!.Name = value;
        }
    }

    public static implicit operator Object(EngineObject target) => new Object(target);

    /// <summary>Queues the wrapped object for destruction at the end of the frame.</summary>
    public static void Destroy(Object obj)
    {
        if (obj is null) return;
        DestroyTarget(obj.Target);
    }

    /// <summary>Queues a Prowl engine object for destruction at the end of the frame.</summary>
    public static void Destroy(EngineObject obj) => DestroyTarget(obj);

    /// <summary>Destroys the wrapped object immediately.</summary>
    public static void DestroyImmediate(Object obj)
    {
        if (obj is null) return;
        DestroyTargetImmediate(obj.Target);
    }

    /// <summary>Destroys a Prowl engine object immediately.</summary>
    public static void DestroyImmediate(EngineObject obj) => DestroyTargetImmediate(obj);

    /// <summary>
    /// Unity-style clone. Prowl can clone GameObjects and prefab assets, so only wrappers around those are
    /// instantiable; other engine objects report the failure through <see cref="Prowl.Runtime.Debug"/>.
    /// </summary>
    public static T? Instantiate<T>(T original) where T : Object, new()
    {
        if (original is null || original.Target.IsNotValid())
        {
            Prowl.Runtime.Debug.LogError("[UnityCompat] Instantiate called with a null or invalid original.");
            return default;
        }

        EngineObject? instance = InstantiateTarget(original.Target);
        if (instance.IsNotValid()) return default;

        return new T { Target = instance };
    }

    /// <summary>Non-generic counterpart of <see cref="Instantiate{T}"/>.</summary>
    public static Object? Instantiate(Object original)
    {
        if (original is null || original.Target.IsNotValid())
        {
            Prowl.Runtime.Debug.LogError("[UnityCompat] Instantiate called with a null or invalid original.");
            return null;
        }

        EngineObject? instance = InstantiateTarget(original.Target);
        return instance.IsValid() ? new Object(instance) : null;
    }

    /// <summary>Instantiates a Prowl engine object and returns it wrapped for Unity-style code.</summary>
    public static Object? Instantiate(EngineObject original)
    {
        EngineObject? instance = InstantiateTarget(original);
        return instance.IsValid() ? new Object(instance) : null;
    }

    public override string ToString() => Name;

    private static void DestroyTarget(EngineObject? target)
    {
        if (target.IsNotValid()) return;

        if (target is Asset { IsFromDatabase: true })
        {
            Prowl.Runtime.Debug.LogError($"[UnityCompat] '{target!.Name}' belongs to the asset database; use AssetDatabase.Unload instead.");
            return;
        }

        target!.Destroy();
    }

    private static void DestroyTargetImmediate(EngineObject? target)
    {
        if (target.IsNotValid()) return;

        if (target is Asset { IsFromDatabase: true })
        {
            Prowl.Runtime.Debug.LogError($"[UnityCompat] '{target!.Name}' belongs to the asset database; use AssetDatabase.Unload instead.");
            return;
        }

        target!.Dispose();
    }

    private static EngineObject? InstantiateTarget(EngineObject? original)
    {
        if (original.IsNotValid()) return null;

        return original switch
        {
            GameObject gameObject => GameObject.Instantiate(gameObject),
            Prowl.Runtime.Resources.PrefabAsset prefab => GameObject.Instantiate(prefab),
            _ => null
        };
    }
}

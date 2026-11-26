// Unity-style MonoBehaviour mapped onto Prowl.Runtime.Component.

using Prowl.Runtime;

namespace UnityEngine;

/// <summary>
/// Base class for Unity-style scripts. It derives directly from <see cref="Component"/>, so the component is
/// a real Prowl component and Prowl's dispatcher drives it.
/// <para/>
/// <c>Start()</c>, <c>Update()</c>, <c>OnEnable()</c> and <c>OnDisable()</c> already exist on
/// <see cref="Component"/> with the exact Unity signatures, so they are inherited unchanged: a Unity-style
/// override is dispatched by Prowl as-is. Only <c>Awake()</c> has no Prowl counterpart and needs a forwarder.
/// </summary>
public abstract class MonoBehaviour : Component
{
    private bool _awakeInvoked;

    /// <summary>
    /// Unity's creation callback. Forwarded once from <see cref="Component.OnAddedToScene"/>, which is the
    /// closest Prowl equivalent (it runs before OnEnable when the GameObject enters a scene).
    /// </summary>
    public virtual void Awake() { }

    public override void OnAddedToScene()
    {
        base.OnAddedToScene();
        if (_awakeInvoked) return;
        _awakeInvoked = true;
        Awake();
    }
}

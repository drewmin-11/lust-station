using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Sunrise.Smoking;

/// <summary>
/// Предмет никотинового пластыря в индивидуальной упаковке.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NicotinePatchItemComponent : Component
{
    [DataField("useTime")]
    public float UseTime = 1.5f;
}

[Serializable, NetSerializable]
public sealed partial class NicotinePatchDoAfterEvent : SimpleDoAfterEvent
{
}

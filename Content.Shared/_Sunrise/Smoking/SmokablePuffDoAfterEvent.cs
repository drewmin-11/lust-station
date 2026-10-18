using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Sunrise.Smoking;

/// <summary>
/// Событие DoAfter для активной затяжки сигаретой, сигарой или трубкой.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SmokablePuffDoAfterEvent : SimpleDoAfterEvent
{
    public bool ReturnToHand;
    public string? HandId;

    public SmokablePuffDoAfterEvent()
    {
    }

    public SmokablePuffDoAfterEvent(bool returnToHand, string? handId)
    {
        ReturnToHand = returnToHand;
        HandId = handId;
    }
}

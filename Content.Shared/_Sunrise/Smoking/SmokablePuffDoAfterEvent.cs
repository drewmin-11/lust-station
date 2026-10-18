using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Sunrise.Smoking;

/// <summary>
/// Событие DoAfter для активной затяжки сигаретой, сигарой или трубкой.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SmokablePuffDoAfterEvent : SimpleDoAfterEvent
{
}

using Content.Shared.Actions;

namespace Content.Shared._Lust.Weapons.Melee;

public sealed partial class KatanaDashActionEvent : WorldTargetActionEvent
{
}

public sealed partial class KatanaCleaveActionEvent : InstantActionEvent
{
}

public sealed partial class KatanaParryActionEvent : InstantActionEvent
{
}

/// <summary>
/// Событие запроса расчленения цели при ударе катаной.
/// </summary>
[ByRefEvent]
public record struct KatanaDismemberEvent(EntityUid Target, EntityUid Attacker, EntityUid Katana);

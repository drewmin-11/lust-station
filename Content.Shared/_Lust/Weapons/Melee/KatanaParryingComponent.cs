using Robust.Shared.GameStates;

namespace Content.Shared._Lust.Weapons.Melee;

/// <summary>
/// Маркерный компонент стойки парирования, накладываемый на персонажа.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KatanaParryingComponent : Component
{
    /// <summary>
    /// Катана, которой выполняется парирование.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid Katana;

    /// <summary>
    /// Время окончания стойки парирования.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan EndTime;
}

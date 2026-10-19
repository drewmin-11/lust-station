using Robust.Shared.GameStates;

using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._Sunrise.Smoking;

/// <summary>
/// Маркер картриджа для электронных вейпов, отслеживающий раствор и визуальное состояние.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VapeCartridgeComponent : Component
{
    /// <summary>
    /// ID раствора, используемого картриджем.
    /// </summary>
    [DataField("solution")]
    public string Solution = "smokable";

    /// <summary>
    /// Соответствие преобладающего вкусового реагента визуальному суффиксу ("mint", "berry", "tobacco", "banana").
    /// </summary>
    [DataField("flavorVisuals")]
    public Dictionary<ProtoId<ReagentPrototype>, string> FlavorVisuals = new()
    {
        { "JuiceLime", "mint" },
        { "JuiceBerry", "berry" },
        { "Coffee", "tobacco" },
        { "JuiceBanana", "banana" },
    };

    /// <summary>
    /// Текущее визуальное состояние жидкости ("empty", "mint", "berry", etc.).
    /// </summary>
    [DataField]
    public string CurrentFlavor = "empty";
}

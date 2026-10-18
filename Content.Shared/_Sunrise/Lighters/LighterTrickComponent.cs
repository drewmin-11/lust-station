using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Sunrise.Lighters;

/// <summary>
/// Компонент для выполнения трюков с Zippo/Flippo зажигалкой.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LighterTrickComponent : Component
{
    /// <summary>
    /// Базовый шанс уронить зажигалку при трюке (7%).
    /// </summary>
    [DataField("failChance")]
    [AutoNetworkedField]
    public float FailChance = 0.07f;

    /// <summary>
    /// Шанс неудачи для неуклюжих персонажей (клоунов и т.д.).
    /// </summary>
    [DataField("clumsyFailChance")]
    [AutoNetworkedField]
    public float ClumsyFailChance = 0.35f;

    /// <summary>
    /// Задержка между выполнением трюков.
    /// </summary>
    [DataField("cooldown")]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1.2);

    [DataField("nextTrickTime")]
    public TimeSpan NextTrickTime = TimeSpan.Zero;
}

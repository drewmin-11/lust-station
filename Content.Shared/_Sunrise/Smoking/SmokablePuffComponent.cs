using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Sunrise.Smoking;

/// <summary>
/// Компонент для активной затяжки тлеющими сигаретами, сигарами и трубками.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class SmokablePuffComponent : Component
{
    [DataField, AutoPausedField]
    public TimeSpan LastPuffTime = TimeSpan.Zero;

    [DataField("cooldown")]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Объем раствора, сгораемый/вдыхаемый за одну затяжку.
    /// </summary>
    [DataField("puffVolume")]
    public float PuffVolume = 0.5f;

    /// <summary>
    /// Длительность затяжки при курении себя.
    /// </summary>
    [DataField("puffDelay")]
    public float PuffDelay = 1.2f;

    /// <summary>
    /// Длительность затяжки при принудительном курении другого персонажа.
    /// </summary>
    [DataField("forcedPuffDelay")]
    public float ForcedPuffDelay = 2.5f;

    /// <summary>
    /// Звук глубокой затяжки тлеющим табаком.
    /// </summary>
    [DataField("puffSound")]
    public SoundSpecifier? PuffSound = new SoundPathSpecifier("/Audio/_Sunrise/Effects/cigarette_puff.ogg");

    /// <summary>
    /// Звук выдоха табачного дыма.
    /// </summary>
    [DataField("exhaleSound")]
    public SoundSpecifier? ExhaleSound = new SoundPathSpecifier("/Audio/_Sunrise/Effects/cigarette_exhale.ogg");

    /// <summary>
    /// Прототип действия запуска колечка дыма.
    /// </summary>
    [DataField("smokeRingAction")]
    public EntProtoId SmokeRingAction = "ActionBlowSmokeRing";

    [DataField("smokeRingActionEntity")]
    public EntityUid? SmokeRingActionEntity;
}

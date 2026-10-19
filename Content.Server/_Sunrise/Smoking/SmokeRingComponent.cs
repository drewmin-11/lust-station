using Content.Shared.Atmos;

namespace Content.Server._Sunrise.Smoking;

/// <summary>
/// Компонент летящего колечка дыма или пара, реагирующий на столкновения и распад.
/// </summary>
[RegisterComponent]
public sealed partial class SmokeRingComponent : Component
{
    [DataField("dissipated")]
    public bool Dissipated = false;

    /// <summary>
    /// Персонаж, выпустивший колечко.
    /// </summary>
    [DataField]
    public EntityUid? Shooter;

    /// <summary>
    /// Стул, кресло или мебель, на которой сидит персонаж при выстреле.
    /// </summary>
    [DataField]
    public EntityUid? IgnoredSeat;

    /// <summary>
    /// Время создания колечка.
    /// </summary>
    [DataField]
    public TimeSpan SpawnTime = TimeSpan.Zero;

    /// <summary>
    /// Цвет колечка при полете.
    /// </summary>
    [DataField("vaporColor")]
    public Color VaporColor = Color.White;

    /// <summary>
    /// Атмосферный газ, выделяемый при распаде колечка (водяной пар или табачный дым).
    /// </summary>
    [DataField("gasType")]
    public Gas GasType = Gas.WaterVapor;
}

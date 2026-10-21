using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Lust.Weapons.Melee;

/// <summary>
/// Компонент самурайской катаны с поддержкой комбо, парирования, рассечения и отрубания конечностей.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KatanaComponent : Component
{
    /// <summary>
    /// Шанс отрубания конечности при попадании (20%).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DismemberChance = 0.20f;

    /// <summary>
    /// Текущий счётчик ударов в комбо-серии.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ComboCount;

    /// <summary>
    /// Время последнего удара для сброса комбо.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan LastAttackTime = TimeSpan.Zero;

    /// <summary>
    /// Интервал времени, после которого серия комбо сбрасывается.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ComboResetTime = TimeSpan.FromSeconds(2.0);

    /// <summary>
    /// Множитель урона для 3-го удара (комбо-финишера).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ComboBonusMultiplier = 1.6f;

    /// <summary>
    /// Активен ли бонус первого удара после извлечения из ножен.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool DrawBonusActive = true;

    /// <summary>
    /// Множитель урона для первого удара из ножен.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DrawBonusMultiplier = 1.5f;

    /// <summary>
    /// Длительность защитной стойки парирования.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ParryDuration = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Дистанция рывка Иайдо.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DashRange = 5.0f;

    /// <summary>
    /// Урон, наносимый врагам на пути рывка Иайдо.
    /// </summary>
    [DataField]
    public DamageSpecifier DashDamage = new()
    {
        DamageDict = new() { { "Slash", 25 } }
    };

    /// <summary>
    /// Радиус кругового рассекающего удара (Cleave).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CleaveRadius = 2.5f;

    /// <summary>
    /// Урон кругового рассекающего удара (Cleave).
    /// </summary>
    [DataField]
    public DamageSpecifier CleaveDamage = new()
    {
        DamageDict = new() { { "Slash", 30 } }
    };

    /// <summary>
    /// Звук парирования / отбивания удара или пули.
    /// </summary>
    [DataField]
    public SoundSpecifier ParrySound = new SoundPathSpecifier("/Audio/_Lust/Weapons/Katana/katana_parry.ogg");

    /// <summary>
    /// Звук отсечения плоти / расчленения.
    /// </summary>
    [DataField]
    public SoundSpecifier DismemberSound = new SoundPathSpecifier("/Audio/_Lust/Weapons/Katana/katana_flesh_sever.ogg");

    /// <summary>
    /// Звук извлечения клинка из ножен.
    /// </summary>
    [DataField]
    public SoundSpecifier DrawSound = new SoundPathSpecifier("/Audio/Items/unsheath.ogg");

    /// <summary>
    /// Звук возвращения клинка в ножны.
    /// </summary>
    [DataField]
    public SoundSpecifier SheatheSound = new SoundPathSpecifier("/Audio/Items/sheath.ogg");

    /// <summary>
    /// Звук кругового рассечения.
    /// </summary>
    [DataField]
    public SoundSpecifier CleaveSound = new SoundPathSpecifier("/Audio/_Lust/Weapons/Katana/katana_swing.ogg");

    /// <summary>
    /// Прототип эффекта попадания клинка.
    /// </summary>
    [DataField]
    public EntProtoId HitEffect = "EffectKatanaSlash";

    /// <summary>
    /// Прототип эффекта рывка Иайдо.
    /// </summary>
    [DataField]
    public EntProtoId DashEffect = "EffectKatanaDash";

    /// <summary>
    /// Прототип эффекта кругового удара 360°.
    /// </summary>
    [DataField]
    public EntProtoId CleaveEffect = "EffectKatanaCleave";

    /// <summary>
    /// Прототип эффекта парирования.
    /// </summary>
    [DataField]
    public EntProtoId ParryEffect = "EffectKatanaParry";

    /// <summary>
    /// Прототип эффекта отрубания конечности.
    /// </summary>
    [DataField]
    public EntProtoId SeverEffect = "EffectKatanaSever";

    /// <summary>
    /// Прототип действия рывка Иайдо.
    /// </summary>
    [DataField]
    public EntProtoId DashAction = "ActionKatanaDash";

    [DataField, AutoNetworkedField]
    public EntityUid? DashActionEntity;

    /// <summary>
    /// Прототип действия кругового удара.
    /// </summary>
    [DataField]
    public EntProtoId CleaveAction = "ActionKatanaCleave";

    [DataField, AutoNetworkedField]
    public EntityUid? CleaveActionEntity;

    /// <summary>
    /// Прототип действия стойки парирования.
    /// </summary>
    [DataField]
    public EntProtoId ParryAction = "ActionKatanaParry";

    [DataField, AutoNetworkedField]
    public EntityUid? ParryActionEntity;
}

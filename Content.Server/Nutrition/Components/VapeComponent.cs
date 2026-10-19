using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Atmos;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server.Nutrition.Components
{
    /// <summary>
    /// Компонент для электронного испарителя (вейпа).
    /// Использует движковый ItemSlots для сменного картриджа с раствором.
    /// </summary>
    [RegisterComponent, AutoGenerateComponentPause, Access(typeof(SmokingSystem))]
    public sealed partial class VapeComponent : Component
    {
        /// <summary>
        /// Время doafter затяжки при использовании на других персонажах.
        /// </summary>
        [DataField("delay")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float Delay { get; set; } = 3.0f;

        /// <summary>
        /// Время doafter затяжки при парении на себе.
        /// </summary>
        [DataField("userDelay")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float UserDelay { get; set; } = 1.2f;

        /// <summary>
        /// Минимальный интервал между затяжками.
        /// </summary>
        [DataField("cooldown")]
        public TimeSpan Cooldown = TimeSpan.FromSeconds(1.5);

        /// <summary>
        /// Время последней затяжки вейпом. Учитывает паузу сервера/энтити.
        /// </summary>
        [DataField, AutoPausedField]
        public TimeSpan LastPuffTime = TimeSpan.Zero;

        [DataField("explosionIntensity")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float ExplosionIntensity { get; set; } = 2.5f;

        [DataField("explodeOnUse")]
        [ViewVariables(VVAccess.ReadWrite)]
        public bool ExplodeOnUse { get; set; } = false;

        [DataField("damage", required: true)]
        [ViewVariables(VVAccess.ReadWrite)]
        public DamageSpecifier Damage = default!;

        [DataField("gasType")]
        [ViewVariables(VVAccess.ReadWrite)]
        public Gas GasType { get; set; } = Gas.WaterVapor;

        /// <summary>
        /// Название слота ItemSlots для вставки картриджа.
        /// </summary>
        [DataField("cartridgeSlot")]
        [ViewVariables(VVAccess.ReadWrite)]
        public string CartridgeSlot { get; set; } = "cartridge";

        /// <summary>
        /// Объем жидкости, выпариваемый за одну затяжку.
        /// </summary>
        [DataField("puffVolume")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float PuffVolume { get; set; } = 1.0f;

        /// <summary>
        /// Моли пара, выделяемые в атмосферу при выдохе.
        /// </summary>
        [DataField("vaporMolesPerPuff")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float VaporMolesPerPuff { get; set; } = 2.0f;

        /// <summary>
        /// Звук затяжки вейпом (шипение спирали).
        /// </summary>
        [DataField("puffSound")]
        public SoundSpecifier? PuffSound = new SoundPathSpecifier("/Audio/_Sunrise/Effects/vape_puff.ogg");

        /// <summary>
        /// Звук выдоха пара.
        /// </summary>
        [DataField("exhaleSound")]
        public SoundSpecifier? ExhaleSound = new SoundPathSpecifier("/Audio/_Sunrise/Effects/cigarette_exhale.ogg");

        /// <summary>
        /// Прототип действия запуска колечка пара.
        /// </summary>
        [DataField("smokeRingAction")]
        public EntProtoId SmokeRingAction = "ActionBlowSmokeRing";

        [DataField("smokeRingActionEntity")]
        public EntityUid? SmokeRingActionEntity;
    }
}

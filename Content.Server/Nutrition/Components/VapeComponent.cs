using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Atmos;

namespace Content.Server.Nutrition.Components // Vapes are very nutritious.
{
    [RegisterComponent, Access(typeof(SmokingSystem))]
    public sealed partial class VapeComponent : Component
    {
        [DataField("delay")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float Delay { get; set; } = 5;

        [DataField("userDelay")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float UserDelay { get; set; } = 2;

        [DataField("explosionIntensity")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float ExplosionIntensity { get; set; } = 2.5f;

        // TODO use RiggableComponent.
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
        /// Solution volume will be divided by this number and converted to the gas
        /// </summary>
        [DataField("reductionFactor")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float ReductionFactor { get; set; } = 300f;

        // TODO when this gets fixed, use prototype serializers
        [DataField("solutionNeeded")]
        [ViewVariables(VVAccess.ReadWrite)]
        public string SolutionNeeded = "Water";

        // Sunrise-Start
        [DataField("cartridgeSlot")]
        [ViewVariables(VVAccess.ReadWrite)]
        public string CartridgeSlot { get; set; } = "cartridge";

        [DataField("puffVolume")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float PuffVolume { get; set; } = 1.5f;

        /// <summary>
        /// Количество молей водяного пара за одну затяжку вейпом.
        /// </summary>
        [DataField("vaporMolesPerPuff")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float VaporMolesPerPuff { get; set; } = 45.0f;

        /// <summary>
        /// Звук затяжки вейпом (шипение спирали и плотный обдув).
        /// </summary>
        [DataField("puffSound")]
        public Robust.Shared.Audio.SoundSpecifier? PuffSound = new Robust.Shared.Audio.SoundPathSpecifier("/Audio/_Sunrise/Effects/vape_puff.ogg");

        /// <summary>
        /// Звук выдоха клубов пара.
        /// </summary>
        [DataField("exhaleSound")]
        public Robust.Shared.Audio.SoundSpecifier? ExhaleSound = new Robust.Shared.Audio.SoundPathSpecifier("/Audio/_Sunrise/Effects/vape_exhale.ogg");
        // Sunrise-End
    }
}

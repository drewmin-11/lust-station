using Content.Shared.FixedPoint;
using Content.Shared.Smoking;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Nutrition.Components
{
    [RegisterComponent, NetworkedComponent]
    public sealed partial class SmokableComponent : Component
    {
        [DataField("solution")]
        public string Solution { get; private set; } = "smokable";

        /// <summary>
        ///     Solution inhale amount per second.
        /// </summary>
        [DataField("inhaleAmount"), ViewVariables(VVAccess.ReadWrite)]
        public FixedPoint2 InhaleAmount { get; private set; } = FixedPoint2.New(0.05f);

        [DataField("state")]
        public SmokableState State { get; set; } = SmokableState.Unlit;

        [DataField("exposeTemperature"), ViewVariables(VVAccess.ReadWrite)]
        public float ExposeTemperature { get; set; } = 0;

        [DataField("exposeVolume"), ViewVariables(VVAccess.ReadWrite)]
        public float ExposeVolume { get; set; } = 1f;

        // clothing prefixes
        [DataField("burntPrefix")]
        public string BurntPrefix = "unlit";
        [DataField("litPrefix")]
        public string LitPrefix = "lit";
        [DataField("unlitPrefix")]
        public string UnlitPrefix = "unlit";

        /// <summary>
        /// Sound played when lighting this smokable.
        /// </summary>
        [DataField]
        public SoundSpecifier? LightSound = new SoundPathSpecifier("/Audio/Effects/cig_light.ogg");

        /// <summary>
        /// Sound played when this smokable is extinguished or runs out.
        /// </summary>
        [DataField]
        public SoundSpecifier? SnuffSound = new SoundPathSpecifier("/Audio/Effects/cig_snuff.ogg");

        // Sunrise added start - Механика активной затяжки и дыма
        /// <summary>
        /// Количество раствора, вдыхаемое за одну активную затяжку.
        /// </summary>
        [DataField("puffAmount"), ViewVariables(VVAccess.ReadWrite)]
        public FixedPoint2 PuffAmount = FixedPoint2.New(0.5f);

        /// <summary>
        /// Количество молей сигаретного дыма, выпускаемое за одну затяжку.
        /// </summary>
        [DataField("smokeMolesPerPuff"), ViewVariables(VVAccess.ReadWrite)]
        public float SmokeMolesPerPuff = 0.6f;

        /// <summary>
        /// Время затяжки при курении себя (в секундах).
        /// </summary>
        [DataField("puffDelay"), ViewVariables(VVAccess.ReadWrite)]
        public float PuffDelay = 1.5f;

        /// <summary>
        /// Время затяжки при затягивании другого персонажа (в секундах).
        /// </summary>
        [DataField("forcedPuffDelay"), ViewVariables(VVAccess.ReadWrite)]
        public float ForcedPuffDelay = 3.0f;

        /// <summary>
        /// Звук глубокой затяжки тлеющим табаком при начале затяжки.
        /// </summary>
        [DataField("puffSound")]
        public SoundSpecifier? PuffSound = new SoundPathSpecifier("/Audio/_Sunrise/Effects/cigarette_puff.ogg");

        /// <summary>
        /// Звук выдоха табачного дыма после затяжки.
        /// </summary>
        [DataField("exhaleSound")]
        public SoundSpecifier? ExhaleSound = new SoundPathSpecifier("/Audio/_Sunrise/Effects/cigarette_exhale.ogg");
        // Sunrise added end
    }
}

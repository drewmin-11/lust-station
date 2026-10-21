using Content.Server._Starlight.Medical.Limbs;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Stunnable;
using Content.Shared._Lust.Weapons.Melee;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._Lust.Weapons.Melee;

/// <summary>
/// Серверная система катаны, отвечающая за отрубание конечностей, кровотечение, лужи крови и нокдаун.
/// </summary>
public sealed partial class KatanaSystem : SharedKatanaSystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LimbSystem _limbSystem = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StunSystem _stun = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    [Dependency] private EntityQuery<BodyComponent> _bodyQuery = default!;
    [Dependency] private EntityQuery<OrganComponent> _organQuery = default!;
    [Dependency] private EntityQuery<KatanaComponent> _katanaQuery = default!;

    private static readonly string[] LimbCategories =
    [
        "ArmLeft",
        "ArmRight",
        "HandLeft",
        "HandRight",
        "LegLeft",
        "LegRight",
        "FootLeft",
        "FootRight"
    ];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KatanaDismemberEvent>(OnDismemberRequested);
    }

    private void OnDismemberRequested(ref KatanaDismemberEvent args)
    {
        TryDismember(args.Target, args.Attacker, args.Katana);
    }

    public bool TryDismember(EntityUid target, EntityUid attacker, EntityUid katanaUid)
    {
        if (!_bodyQuery.TryComp(target, out var body) || body.Organs == null)
            return false;

        if (!_katanaQuery.TryComp(katanaUid, out var katana))
            return false;

        // Ищем все отсекаемые внешние конечности
        var candidates = new List<(EntityUid Uid, string Category)>();

        foreach (var organUid in body.Organs.ContainedEntities)
        {
            if (!_organQuery.TryComp(organUid, out var organComp) || organComp.Category == null)
                continue;

            var cat = organComp.Category.Value.Id;
            foreach (var validCat in LimbCategories)
            {
                if (cat == validCat)
                {
                    candidates.Add((organUid, cat));
                    break;
                }
            }
        }

        if (candidates.Count == 0)
            return false;

        // Выбираем случайную конечность
        var (chosenLimb, category) = _random.Pick(candidates);

        // Вызываем ампутацию через систему конечностей
        if (!_limbSystem.Amputate((target, body), chosenLimb))
            return false;

        var isArm = category.StartsWith("Arm") || category.StartsWith("Hand");
        var isLeg = category.StartsWith("Leg") || category.StartsWith("Foot");

        // Звук отсечения плоти
        _audio.PlayPvs(katana.DismemberSound, target);

        // Брызги и лужа крови
        var targetCoords = Transform(target).Coordinates;
        var blood = new Solution();
        blood.AddReagent(new ReagentId("Blood", null), FixedPoint2.New(15));
        _puddle.TrySpillAt(targetCoords, blood, out _);

        // Сильное кровотечение
        _bloodstream.TryModifyBleedAmount(target, 12f);

        var targetName = Identity.Entity(target, EntityManager);

        if (isArm)
        {
            // Отрублена рука — выбиваем предмет
            _hands.DropAll(target);
            _popup.PopupEntity(Loc.GetString("katana-dismember-arm", ("target", targetName)), target);
        }
        else if (isLeg)
        {
            // Отрублена нога — сбиваем с ног
            _stun.TryKnockdown(target, TimeSpan.FromSeconds(3.5), true);
            _popup.PopupEntity(Loc.GetString("katana-dismember-leg", ("target", targetName)), target);
        }

        return true;
    }
}

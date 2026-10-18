using Content.Shared._Sunrise.Lighters;
using Content.Shared.Clumsy;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Sunrise.Lighters;

/// <summary>
/// Система выполнения трюков с Zippo и Flippo зажигалками.
/// </summary>
public sealed partial class LighterTrickSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemToggleSystem _itemToggle = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ProtoId<DamageTypePrototype> HeatDamageType = "Heat";

    private static readonly SoundPathSpecifier OpenSound = new("/Audio/Items/Lighters/zippo_open.ogg");
    private static readonly SoundPathSpecifier CloseSound = new("/Audio/Items/Lighters/zippo_close.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LighterTrickComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
    }

    private void OnGetAlternativeVerbs(Entity<LighterTrickComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;
        var verb = new AlternativeVerb
        {
            Text = Loc.GetString("lighter-trick-verb"),
            Act = () => TryDoTrick(entity, user),
            Priority = 2
        };

        args.Verbs.Add(verb);
    }

    /// <summary>
    /// Попытка выполнить трюк с зажигалкой.
    /// </summary>
    public bool TryDoTrick(Entity<LighterTrickComponent> entity, EntityUid user)
    {
        if (!CanDoTrick(entity, user))
            return false;

        DoTrick(entity, user);
        return true;
    }

    /// <summary>
    /// Проверка возможности совершить трюк с зажигалкой.
    /// </summary>
    public bool CanDoTrick(Entity<LighterTrickComponent> entity, EntityUid user, bool quiet = false)
    {
        // Персонаж должен держать зажигалку в руках
        if (!_hands.IsHolding(user, entity.Owner, out _))
        {
            if (!quiet)
                _popup.PopupEntity(Loc.GetString("lighter-trick-not-in-hands"), user, user);
            return false;
        }

        // Проверяем кулдаун
        if (_timing.CurTime < entity.Comp.NextTrickTime)
            return false;

        return true;
    }

    /// <summary>
    /// Непосредственное выполнение трюка.
    /// </summary>
    private void DoTrick(Entity<LighterTrickComponent> entity, EntityUid user)
    {
        entity.Comp.NextTrickTime = _timing.CurTime + entity.Comp.Cooldown;

        var isLit = TryComp<ItemToggleComponent>(entity.Owner, out var toggle) && toggle.Activated;
        var failChance = HasComp<ClumsyComponent>(user) ? entity.Comp.ClumsyFailChance : entity.Comp.FailChance;

        var userName = Identity.Entity(user, EntityManager);
        var itemName = Identity.Entity(entity.Owner, EntityManager);

        // Случайный провал
        if (_random.Prob(failChance))
        {
            if (isLit)
            {
                // Ожог пальцев и тушение
                var burnDamage = new DamageSpecifier(_proto.Index<DamageTypePrototype>(HeatDamageType), 1);
                _damageable.TryChangeDamage(user, burnDamage, origin: entity.Owner);

                _popup.PopupEntity(Loc.GetString("lighter-trick-fail-burn", ("user", userName), ("item", itemName)), user, PopupType.MediumCaution);
                _itemToggle.TryDeactivate(entity.Owner, user, predicted: false);
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("lighter-trick-fail-drop", ("user", userName), ("item", itemName)), user, PopupType.SmallCaution);
            }

            _audio.PlayPvs(CloseSound, user, AudioParams.Default.WithVolume(-2f).WithVariation(0.05f));
            _hands.TryDrop(user, entity.Owner);
            return;
        }

        // Успешный трюк
        if (!isLit)
        {
            // Открываем и зажигаем с трюком
            _itemToggle.TryActivate(entity.Owner, user, predicted: false);
            _audio.PlayPvs(OpenSound, user, AudioParams.Default.WithVolume(-2f).WithVariation(0.05f));

            var phraseIndex = _random.Next(1, 4);
            var locString = Loc.GetString($"lighter-trick-open-{phraseIndex}", ("user", userName), ("item", itemName));
            _popup.PopupEntity(locString, user);
        }
        else
        {
            // Эффектно гасим и закрываем с трюком
            _itemToggle.TryDeactivate(entity.Owner, user, predicted: false);
            _audio.PlayPvs(CloseSound, user, AudioParams.Default.WithVolume(-2f).WithVariation(0.05f));

            var phraseIndex = _random.Next(1, 4);
            var locString = Loc.GetString($"lighter-trick-close-{phraseIndex}", ("user", userName), ("item", itemName));
            _popup.PopupEntity(locString, user);
        }
    }
}

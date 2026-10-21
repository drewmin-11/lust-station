using System.Numerics;
using Content.Shared._Lust.Weapons.Melee;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Lust.Weapons.Melee;

/// <summary>
/// Общая система управления самурайской катаной: комбо-серии, стойка парирования, рывок Иайдо и круговой взмах.
/// </summary>
public abstract partial class SharedKatanaSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private PullingSystem _pulling = default!;

    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;
    [Dependency] private EntityQuery<WieldableComponent> _wieldableQuery = default!;
    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;
    [Dependency] private EntityQuery<KatanaComponent> _katanaQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KatanaComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<KatanaComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<KatanaComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<KatanaComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<KatanaComponent, GotEquippedHandEvent>(OnEquippedHand);
        SubscribeLocalEvent<KatanaComponent, GotUnequippedHandEvent>(OnUnequippedHand);
        SubscribeLocalEvent<KatanaComponent, MeleeHitEvent>(OnMeleeHit);

        SubscribeLocalEvent<KatanaComponent, KatanaDashActionEvent>(OnDashAction);
        SubscribeLocalEvent<KatanaComponent, KatanaCleaveActionEvent>(OnCleaveAction);
        SubscribeLocalEvent<KatanaComponent, KatanaParryActionEvent>(OnParryAction);

        SubscribeLocalEvent<KatanaParryingComponent, DamageModifyEvent>(OnParryDamageModify);
        SubscribeLocalEvent<KatanaParryingComponent, BeforeDamageChangedEvent>(OnParryBeforeDamageChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Проверяем окончание стойки парирования у сущностей
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<KatanaParryingComponent>();
        while (query.MoveNext(out var uid, out var parry))
        {
            if (curTime >= parry.EndTime)
            {
                RemCompDeferred<KatanaParryingComponent>(uid);
                _popup.PopupClient(Loc.GetString("katana-parry-stance-end"), uid, uid);
            }
        }
    }

    private void OnMapInit(Entity<KatanaComponent> ent, ref MapInitEvent args)
    {
        var (uid, comp) = ent;
        _actionContainer.EnsureAction(uid, ref comp.DashActionEntity, comp.DashAction);
        _actionContainer.EnsureAction(uid, ref comp.CleaveActionEntity, comp.CleaveAction);
        _actionContainer.EnsureAction(uid, ref comp.ParryActionEntity, comp.ParryAction);
        Dirty(uid, comp);
    }

    private void OnGetActions(Entity<KatanaComponent> ent, ref GetItemActionsEvent args)
    {
        if (_wieldableQuery.TryComp(ent.Owner, out var wieldable) && wieldable.Wielded)
        {
            args.AddAction(ent.Comp.DashActionEntity);
            args.AddAction(ent.Comp.CleaveActionEntity);
            args.AddAction(ent.Comp.ParryActionEntity);
        }
    }

    private void OnWielded(Entity<KatanaComponent> ent, ref ItemWieldedEvent args)
    {
        _actions.GrantContainedActions(args.User, ent.Owner);
    }

    private void OnUnwielded(Entity<KatanaComponent> ent, ref ItemUnwieldedEvent args)
    {
        _actions.RemoveProvidedActions(args.User, ent.Owner);

        // Снимаем парирование при снятии двуручного хвата
        if (HasComp<KatanaParryingComponent>(args.User))
            RemComp<KatanaParryingComponent>(args.User);
    }

    private void OnEquippedHand(Entity<KatanaComponent> ent, ref GotEquippedHandEvent args)
    {
        ent.Comp.DrawBonusActive = true;
        Dirty(ent.Owner, ent.Comp);

        // Звук извлечения клинка из ножен
        _audio.PlayPredicted(ent.Comp.DrawSound, ent.Owner, args.User);
    }

    private void OnUnequippedHand(Entity<KatanaComponent> ent, ref GotUnequippedHandEvent args)
    {
        // Звук возвращения клинка в ножны
        _audio.PlayPredicted(ent.Comp.SheatheSound, ent.Owner, args.User);

        if (HasComp<KatanaParryingComponent>(args.User))
            RemComp<KatanaParryingComponent>(args.User);
    }

    private void OnMeleeHit(Entity<KatanaComponent> ent, ref MeleeHitEvent args)
    {
        if (args.HitEntities.Count == 0)
            return;

        var curTime = _timing.CurTime;
        var (katanaUid, comp) = ent;

        // Сброс комбо по таймауту
        if (curTime - comp.LastAttackTime > comp.ComboResetTime)
            comp.ComboCount = 0;

        comp.ComboCount++;
        comp.LastAttackTime = curTime;

        var isComboFinisher = comp.ComboCount >= 3;
        var isDrawStrike = comp.DrawBonusActive;

        if (isComboFinisher)
            comp.ComboCount = 0;

        if (isDrawStrike)
            comp.DrawBonusActive = false;

        Dirty(katanaUid, comp);

        // Расчёт дополнительного урона комбо или первого удара
        var damageMultiplier = 1.0f;
        if (isComboFinisher)
        {
            damageMultiplier *= comp.ComboBonusMultiplier;
            _popup.PopupPredicted(Loc.GetString("katana-combo-finisher"), args.User, args.User);
            _audio.PlayPredicted(comp.CleaveSound, katanaUid, args.User);
        }
        else if (isDrawStrike)
        {
            damageMultiplier *= comp.DrawBonusMultiplier;
            _popup.PopupPredicted(Loc.GetString("katana-draw-strike"), args.User, args.User);
            _audio.PlayPredicted(comp.DrawSound, katanaUid, args.User);
        }

        if (damageMultiplier > 1.0f)
            args.BonusDamage += args.BaseDamage * (damageMultiplier - 1.0f);

        // Проверка отрубания конечностей (на сервере)
        if (_net.IsServer)
        {
            foreach (var hit in args.HitEntities)
            {
                if (_random.Prob(comp.DismemberChance))
                {
                    var ev = new KatanaDismemberEvent(hit, args.User, katanaUid);
                    RaiseLocalEvent(ref ev);
                }
            }
        }
    }

    // ==========================================
    // Спецприём 1: Рывок-разрез Иайдо (Dash)
    // ==========================================

    private void OnDashAction(Entity<KatanaComponent> ent, ref KatanaDashActionEvent args)
    {
        if (args.Handled)
            return;

        if (TryDash(ent, args.Performer, args.Target))
            args.Handled = true;
    }

    public bool TryDash(Entity<KatanaComponent> ent, EntityUid user, EntityCoordinates target)
    {
        if (!CanDash(ent, user, target, out var _))
            return false;

        DoDash(ent, user, target);
        return true;
    }

    public bool CanDash(Entity<KatanaComponent> ent, EntityUid user, EntityCoordinates target, out string? reason)
    {
        reason = null;

        if (!_hands.IsHolding(user, ent.Owner, out _))
        {
            reason = Loc.GetString("katana-dash-not-held");
            _popup.PopupClient(reason, user, user);
            return false;
        }

        if (!_xformQuery.TryComp(user, out var userXform))
            return false;

        var origin = _transform.GetMapCoordinates(user, userXform);
        var targetMap = _transform.ToMapCoordinates(target);

        if (origin.MapId != targetMap.MapId)
            return false;

        var distance = (targetMap.Position - origin.Position).Length();
        if (distance > ent.Comp.DashRange)
        {
            reason = Loc.GetString("katana-dash-too-far");
            _popup.PopupClient(reason, user, user);
            return false;
        }

        if (!_examine.InRangeUnOccluded(origin, targetMap, SharedInteractionSystem.MaxRaycastRange, null))
        {
            reason = Loc.GetString("katana-dash-cant-see");
            _popup.PopupClient(reason, user, user);
            return false;
        }

        return true;
    }

    public void DoDash(Entity<KatanaComponent> ent, EntityUid user, EntityCoordinates target)
    {
        // Освобождаем от захватов
        if (TryComp<PullableComponent>(user, out var pull) && _pulling.IsPulled(user, pull))
            _pulling.TryStopPull(user, pull);

        if (TryComp<PullerComponent>(user, out var puller) && TryComp<PullableComponent>(puller.Pulling, out var pulled))
            _pulling.TryStopPull(puller.Pulling.Value, pulled);

        var userXform = Transform(user);
        var startCoords = userXform.Coordinates;

        // Перемещаем персонажа
        _transform.SetCoordinates(user, userXform, target);
        _transform.AttachToGridOrMap(user, userXform);

        // Звук быстрого рывка
        _audio.PlayPredicted(ent.Comp.CleaveSound, user, user);
        _popup.PopupPredicted(Loc.GetString("katana-dash-success"), user, user);

        // На сервере наносим урон врагам в точке прибытия
        if (_net.IsServer)
        {
            var hitRadius = 1.6f;
            foreach (var nearby in _lookup.GetEntitiesInRange(target, hitRadius))
            {
                if (nearby == user)
                    continue;

                if (!_damageableQuery.HasComp(nearby))
                    continue;

                _damageable.TryChangeDamage(nearby, ent.Comp.DashDamage, origin: user);

                if (_random.Prob(ent.Comp.DismemberChance))
                {
                    var ev = new KatanaDismemberEvent(nearby, user, ent.Owner);
                    RaiseLocalEvent(ref ev);
                }
            }
        }
    }

    // ==========================================
    // Спецприём 2: Круговой взмах (Cleave)
    // ==========================================

    private void OnCleaveAction(Entity<KatanaComponent> ent, ref KatanaCleaveActionEvent args)
    {
        if (args.Handled)
            return;

        if (TryCleave(ent, args.Performer))
            args.Handled = true;
    }

    public bool TryCleave(Entity<KatanaComponent> ent, EntityUid user)
    {
        if (!CanCleave(ent, user, out var _))
            return false;

        DoCleave(ent, user);
        return true;
    }

    public bool CanCleave(Entity<KatanaComponent> ent, EntityUid user, out string? reason)
    {
        reason = null;

        if (!_hands.IsHolding(user, ent.Owner, out _))
        {
            reason = Loc.GetString("katana-dash-not-held");
            _popup.PopupClient(reason, user, user);
            return false;
        }

        return true;
    }

    public void DoCleave(Entity<KatanaComponent> ent, EntityUid user)
    {
        _audio.PlayPredicted(ent.Comp.CleaveSound, user, user);
        _popup.PopupPredicted(Loc.GetString("katana-cleave-success"), user, user);

        if (!_net.IsServer)
            return;

        var userCoords = Transform(user).Coordinates;
        var enemies = _lookup.GetEntitiesInRange(userCoords, ent.Comp.CleaveRadius);

        foreach (var enemy in enemies)
        {
            if (enemy == user)
                continue;

            if (!_damageableQuery.HasComp(enemy))
                continue;

            _damageable.TryChangeDamage(enemy, ent.Comp.CleaveDamage, origin: user);

            // Отталкивание от пользователя
            if (_xformQuery.TryComp(enemy, out var enemyXform))
            {
                var dir = _transform.GetWorldPosition(enemyXform) - _transform.GetWorldPosition(user);
                if (dir.LengthSquared() > 0.001f)
                {
                    var impulse = Vector2.Normalize(dir) * 4.0f;
                    _physics.ApplyLinearImpulse(enemy, impulse);
                }
            }

            if (_random.Prob(ent.Comp.DismemberChance))
            {
                var ev = new KatanaDismemberEvent(enemy, user, ent.Owner);
                RaiseLocalEvent(ref ev);
            }
        }
    }

    // ==========================================
    // Спецприём 3: Стойка парирования (Parry)
    // ==========================================

    private void OnParryAction(Entity<KatanaComponent> ent, ref KatanaParryActionEvent args)
    {
        if (args.Handled)
            return;

        if (TryParry(ent, args.Performer))
            args.Handled = true;
    }

    public bool TryParry(Entity<KatanaComponent> ent, EntityUid user)
    {
        if (!CanParry(ent, user, out var _))
            return false;

        DoParry(ent, user);
        return true;
    }

    public bool CanParry(Entity<KatanaComponent> ent, EntityUid user, out string? reason)
    {
        reason = null;

        if (!_hands.IsHolding(user, ent.Owner, out _))
        {
            reason = Loc.GetString("katana-dash-not-held");
            _popup.PopupClient(reason, user, user);
            return false;
        }

        return true;
    }

    public void DoParry(Entity<KatanaComponent> ent, EntityUid user)
    {
        var parry = EnsureComp<KatanaParryingComponent>(user);
        parry.Katana = ent.Owner;
        parry.EndTime = _timing.CurTime + ent.Comp.ParryDuration;
        Dirty(user, parry);

        _popup.PopupPredicted(Loc.GetString("katana-parry-stance-start"), user, user);
    }

    private void OnParryDamageModify(Entity<KatanaParryingComponent> ent, ref DamageModifyEvent args)
    {
        if (_timing.CurTime >= ent.Comp.EndTime)
            return;

        // Полностью поглощаем урон в стойке парирования
        args.Damage *= 0;

        if (_katanaQuery.TryComp(ent.Comp.Katana, out var katana))
            _audio.PlayPredicted(katana.ParrySound, ent.Owner, args.Origin);

        _popup.PopupPredicted(Loc.GetString("katana-parry-success"), ent.Owner, ent.Owner);
    }

    private void OnParryBeforeDamageChanged(Entity<KatanaParryingComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (_timing.CurTime < ent.Comp.EndTime)
        {
            // Отменяем нанесение урона во время стойки парирования
            args.Cancelled = true;
        }
    }
}

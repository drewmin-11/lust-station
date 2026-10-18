using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.DoAfter;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Popups;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Sunrise.Smoking;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Jittering;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Smoking;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Sunrise.Smoking;

/// <summary>
/// Система активных затяжек сигаретами, сигарами и трубками с выпуском сигаретного дыма в атмосферу.
/// </summary>
public sealed partial class SmokingPuffSystem : EntitySystem
{
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private SmokingSystem _smoking = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedJitteringSystem _jittering = default!;
    [Dependency] private NicotineAddictionSystem _nicotine = default!;
    [Dependency] private StorageSystem _storage = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _lastPuffTime = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SmokableComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<SmokableComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<SmokableComponent, SmokablePuffDoAfterEvent>(OnPuffDoAfter);
        SubscribeLocalEvent<SmokableComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<SmokableComponent, GetVerbsEvent<AlternativeVerb>>(AddCrushVerb);
    }

    /// <summary>
    /// Обработка клика предметом по цели (по себе или другому персонажу).
    /// </summary>
    private void OnAfterInteract(Entity<SmokableComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        // Тушение сигареты о пепельницу
        if (entity.Comp.State == SmokableState.Lit && TryComp<StorageComponent>(args.Target.Value, out var storage) &&
            (HasComp<ItemCounterComponent>(args.Target.Value) || HasComp<DumpableComponent>(args.Target.Value)))
        {
            _smoking.SetSmokableState(entity.Owner, SmokableState.Burnt, entity.Comp);
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/sparks4.ogg"), args.Target.Value, AudioParams.Default.WithVolume(-4f));
            _popup.PopupEntity(Loc.GetString("smoking-ashtray-extinguish"), args.Target.Value, args.User);
            _storage.Insert(args.Target.Value, entity.Owner, out _, storageComp: storage);
            args.Handled = true;
            return;
        }

        if (TryStartPuff(entity, args.User, args.Target.Value))
            args.Handled = true;
    }

    /// <summary>
    /// Обработка использования сигареты в руке (кнопка Z).
    /// </summary>
    private void OnUseInHand(Entity<SmokableComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (TryStartPuff(entity, args.User, args.User))
            args.Handled = true;
    }

    /// <summary>
    /// Попытка начать затяжку с проверкой условий.
    /// </summary>
    public bool TryStartPuff(Entity<SmokableComponent> entity, EntityUid user, EntityUid target)
    {
        if (!CanPuff(entity, user, target, out var reason))
        {
            if (!string.IsNullOrEmpty(reason))
                _popup.PopupEntity(reason, user, user);

            return false;
        }

        var isHeld = _hands.IsHolding(user, entity.Owner, out var hand);
        var returnToHand = false;
        string? handId = null;

        // Проверяем, свободен ли рот / слот маски у цели
        if (_inventory.TryGetSlotEntity(target, "mask", out var inMask))
        {
            // Слот маски занят чем-то отличным от нашей сигареты
            if (inMask != entity.Owner)
            {
                _popup.PopupEntity(Loc.GetString("smoking-mouth-blocked"), user, user);
                return false;
            }
            // Сигарета уже находится в слоте маски
            returnToHand = false;
        }
        else if (isHeld)
        {
            // Слот маски свободен, а сигарета в руке — перемещаем сигарету в слот маски на время затяжки
            if (!_inventory.TryEquip(user, target, entity.Owner, "mask", silent: true, force: true))
            {
                return false;
            }

            returnToHand = true;
            handId = hand;
        }

        var isSelf = (target == user);
        var delay = isSelf ? entity.Comp.PuffDelay : entity.Comp.ForcedPuffDelay;

        // 1. Сочный и плавный звук затяжки тлеющим табаком в момент начала (исходит от куклы курильщика)
        if (entity.Comp.PuffSound != null)
            _audio.PlayPvs(entity.Comp.PuffSound, target, AudioParams.Default.WithVolume(-1.0f).WithMaxDistance(7f).WithVariation(0.04f));

        if (isSelf)
        {
            _popup.PopupEntity(Loc.GetString("smoking-puff-start-self", ("item", entity.Owner)), user, user);
        }
        else
        {
            var userName = Identity.Entity(user, EntityManager);
            var targetName = Identity.Entity(target, EntityManager);
            _popup.PopupEntity(Loc.GetString("smoking-puff-start-target", ("user", userName), ("item", entity.Owner)), target, target);
            _popup.PopupEntity(Loc.GetString("smoking-puff-start-user", ("target", targetName), ("item", entity.Owner)), user, user);
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, user, delay, new SmokablePuffDoAfterEvent(returnToHand, handId), entity.Owner, target: target, used: entity.Owner)
        {
            BreakOnMove = false,
            BreakOnDamage = true,
            NeedHand = false
        };

        return _doAfter.TryStartDoAfter(doAfterArgs);
    }

    /// <summary>
    /// Проверка условий для возможности сделать затяжку.
    /// </summary>
    public bool CanPuff(Entity<SmokableComponent> entity, EntityUid user, EntityUid target, out string? reason)
    {
        reason = null;

        // Предмет должен быть зажжен
        if (entity.Comp.State != SmokableState.Lit)
        {
            reason = Loc.GetString("smoking-puff-unlit", ("item", entity.Owner));
            return false;
        }

        // Проверяем наличие рта и доступность дыхания (шлемы, маски и т.д.)
        if (!HasComp<BloodstreamComponent>(target))
        {
            return false;
        }

        if (!_ingestion.HasMouthAvailable(user, target))
        {
            reason = Loc.GetString("smoking-puff-helmet-blocked");
            return false;
        }

        // Проверяем наличие кислорода и давления в атмосфере
        var environment = _atmos.GetContainingMixture(target, true, true);
        if (environment == null || environment.Pressure < 10f || environment.GetMoles(Gas.Oxygen) < 0.2f)
        {
            _smoking.SetSmokableState(entity.Owner, SmokableState.Unlit, entity.Comp);
            reason = Loc.GetString("smoking-extinguish-vacuum");
            return false;
        }

        // Проверяем наличие оставшегося раствора
        if (!_solution.TryGetSolution(entity.Owner, entity.Comp.Solution, out _, out var solution) ||
            solution.Volume <= FixedPoint2.Zero)
        {
            reason = Loc.GetString("smoking-puff-empty", ("item", entity.Owner));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Выполнение эффекта затяжки по завершении DoAfter.
    /// </summary>
    private void OnPuffDoAfter(Entity<SmokableComponent> entity, ref SmokablePuffDoAfterEvent args)
    {
        if (args.Handled || args.Args.Target == null)
            return;

        var target = args.Args.Target.Value;
        var user = args.Args.User;

        // При отмене DoAfter возвращаем сигарету обратно в руку
        if (args.Cancelled)
        {
            ReturnSmokableToHand(entity.Owner, user, target, args.ReturnToHand, args.HandId);
            return;
        }

        if (entity.Comp.State != SmokableState.Lit)
        {
            ReturnSmokableToHand(entity.Owner, user, target, args.ReturnToHand, args.HandId);
            return;
        }

        if (!_solution.TryGetSolution(entity.Owner, entity.Comp.Solution, out var soln, out var solution))
        {
            ReturnSmokableToHand(entity.Owner, user, target, args.ReturnToHand, args.HandId);
            return;
        }

        // 1. Отщепляем порцию раствора и вводим в кровоток цели
        var puffAmount = FixedPoint2.Min(entity.Comp.PuffAmount, solution.Volume);
        if (puffAmount > FixedPoint2.Zero)
        {
            var inhaled = _solution.SplitSolution(soln.Value, puffAmount);

            if (TryComp<BloodstreamComponent>(target, out var bloodstream))
            {
                _reactive.DoEntityReaction(target, inhaled, ReactionMethod.Ingestion);
                _bloodstream.TryAddToBloodstream((target, bloodstream), inhaled);
            }
        }

        // 2. Выделяем сигаретный дым в атмосферу в тайле персонажа
        var environment = _atmos.GetContainingMixture(target, true, true);
        if (environment != null)
        {
            var smokeMoles = entity.Comp.SmokeMolesPerPuff;
            var merger = new GasMixture(1) { Temperature = Atmospherics.T20C + 15f };
            merger.SetMoles(Gas.TobaccoSmoke, smokeMoles);
            _atmos.Merge(environment, merger);
        }

        // 3. Мягкий звук выдоха дыма (после окончания затяжки, исходит от куклы курильщика)
        if (entity.Comp.ExhaleSound != null)
            _audio.PlayPvs(entity.Comp.ExhaleSound, target, AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(7f).WithVariation(0.04f));

        // 4. Проверка на "паровоз" (слишком частые затяжки подряд)
        var curTime = _timing.CurTime;
        var choked = false;
        if (_lastPuffTime.TryGetValue(target, out var lastTime) && (curTime - lastTime) < TimeSpan.FromSeconds(4.5f))
        {
            choked = true;
            _chat.TryEmoteWithChat(target, "Cough");
            _popup.PopupEntity(Loc.GetString("smoking-puff-choke"), target, target);
            _jittering.DoJitter(target, TimeSpan.FromSeconds(1.5f), true, amplitude: 4f, frequency: 4f);
        }
        _lastPuffTime[target] = curTime;

        // 5. Поп-ап сообщение и шанс колечка дыма (если не подавился)
        var isSelf = (target == user);
        if (!choked)
        {
            if (isSelf && _random.Prob(0.25f))
            {
                _popup.PopupEntity(Loc.GetString("smoking-puff-smoke-ring", ("user", Identity.Entity(user, EntityManager))), user);
            }
            else if (isSelf)
            {
                _popup.PopupEntity(Loc.GetString("smoking-puff-success-self", ("item", entity.Owner)), user, user);
            }
            else
            {
                var userName = Identity.Entity(user, EntityManager);
                var targetName = Identity.Entity(target, EntityManager);
                _popup.PopupEntity(Loc.GetString("smoking-puff-success-other", ("user", userName), ("target", targetName), ("item", entity.Owner)), target);
            }
        }

        // 6. Регистрируем воздействие никотина и зависимость
        _nicotine.RegisterPuff(target);

        // 7. Если раствор закончился — сигарета истлела
        if (solution.Volume <= FixedPoint2.Zero)
        {
            _smoking.SetSmokableState(entity.Owner, SmokableState.Burnt, entity.Comp);
            _popup.PopupEntity(Loc.GetString("smoking-puff-burnt-out", ("item", entity.Owner)), target, user);
        }

        // 8. Возвращаем сигарету/окурок обратно в руку
        ReturnSmokableToHand(entity.Owner, user, target, args.ReturnToHand, args.HandId);

        args.Handled = true;
    }

    /// <summary>
    /// Возвращает курительный предмет из слота маски обратно в руку пользователя.
    /// </summary>
    private void ReturnSmokableToHand(EntityUid smokable, EntityUid user, EntityUid target, bool returnToHand, string? handId)
    {
        if (!returnToHand)
            return;

        if (_inventory.TryGetSlotEntity(target, "mask", out var inMask) && inMask == smokable)
        {
            _inventory.TryUnequip(user, target, "mask", out var unequipped, silent: true, force: true);
            var toReturn = unequipped ?? smokable;
            if (!Deleted(toReturn) && !TerminatingOrDeleted(toReturn))
            {
                if (handId != null && _hands.TryPickup(user, toReturn, handId, animate: false))
                    return;

                _hands.PickupOrDrop(user, toReturn);
            }
        }
    }

    /// <summary>
    /// Возможность прикурить незажженную сигарету от другой зажженной сигареты.
    /// </summary>
    private void OnInteractUsing(Entity<SmokableComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Если эта сигарета не зажжена, а используемый предмет — зажженная сигарета/сигара/трубка
        if (entity.Comp.State == SmokableState.Unlit &&
            TryComp<SmokableComponent>(args.Used, out var usedSmokable) &&
            usedSmokable.State == SmokableState.Lit)
        {
            _smoking.SetSmokableState(entity.Owner, SmokableState.Lit, entity.Comp);

            if (entity.Comp.LightSound != null)
                _audio.PlayPvs(entity.Comp.LightSound, entity.Owner);

            var userName = Identity.Entity(args.User, EntityManager);
            var targetName = Identity.Entity(entity.Owner, EntityManager);
            var usedName = Identity.Entity(args.Used, EntityManager);

            _popup.PopupEntity(Loc.GetString("smoking-light-from-other", ("user", userName), ("target", targetName), ("used", usedName)), entity.Owner);
            args.Handled = true;
        }
    }

    /// <summary>
    /// Альтернативный глагол тушения тлеющей сигареты на полу ботинком.
    /// </summary>
    private void AddCrushVerb(Entity<SmokableComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || entity.Comp.State != SmokableState.Lit)
            return;

        var xform = Transform(entity.Owner);
        if (xform.ParentUid != xform.GridUid && xform.ParentUid != xform.MapUid)
            return;

        var user = args.User;
        var verb = new AlternativeVerb
        {
            Text = Loc.GetString("smoking-verb-crush-boot"),
            Act = () =>
            {
                _smoking.SetSmokableState(entity.Owner, SmokableState.Burnt, entity.Comp);
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Footsteps/dirt1.ogg"), entity.Owner);
                _popup.PopupEntity(Loc.GetString("smoking-crush-boot-popup", ("user", Identity.Entity(user, EntityManager)), ("item", entity.Owner)), entity.Owner);
            },
            Priority = 1
        };
        args.Verbs.Add(verb);
    }
}

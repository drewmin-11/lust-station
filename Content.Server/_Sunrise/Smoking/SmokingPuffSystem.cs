using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.DoAfter;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Popups;
using Content.Shared._Sunrise.Smoking;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Smoking;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SmokableComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<SmokableComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<SmokableComponent, SmokablePuffDoAfterEvent>(OnPuffDoAfter);
        SubscribeLocalEvent<SmokableComponent, InteractUsingEvent>(OnInteractUsing);
    }

    /// <summary>
    /// Обработка клика предметом по цели (по себе или другому персонажу).
    /// </summary>
    private void OnAfterInteract(Entity<SmokableComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

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

        var isSelf = (target == user);
        var delay = isSelf ? entity.Comp.PuffDelay : entity.Comp.ForcedPuffDelay;

        // Звук шипения тлеющего табака
        if (entity.Comp.PuffSound != null)
            _audio.PlayPvs(entity.Comp.PuffSound, entity.Owner);

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

        var doAfterArgs = new DoAfterArgs(EntityManager, user, delay, new SmokablePuffDoAfterEvent(), entity.Owner, target: target, used: entity.Owner)
        {
            BreakOnMove = false,
            BreakOnDamage = true,
            NeedHand = true
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

        // Проверяем наличие рта и доступность дыхания
        if (!HasComp<BloodstreamComponent>(target) || !_ingestion.HasMouthAvailable(user, target))
        {
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
        if (args.Cancelled || args.Handled || args.Args.Target == null)
            return;

        var target = args.Args.Target.Value;
        var user = args.Args.User;

        if (entity.Comp.State != SmokableState.Lit)
            return;

        if (!_solution.TryGetSolution(entity.Owner, entity.Comp.Solution, out var soln, out var solution))
            return;

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

        // 3. Звук выдоха дыма
        if (entity.Comp.ExhaleSound != null)
            _audio.PlayPvs(entity.Comp.ExhaleSound, target);

        // 4. Поп-ап сообщение и шанс колечка дыма
        var isSelf = (target == user);
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

        // 5. Если раствор закончился — сигарета истлела
        if (solution.Volume <= FixedPoint2.Zero)
        {
            _smoking.SetSmokableState(entity.Owner, SmokableState.Burnt, entity.Comp);
            _popup.PopupEntity(Loc.GetString("smoking-puff-burnt-out", ("item", entity.Owner)), target, user);
        }

        args.Handled = true;
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
}

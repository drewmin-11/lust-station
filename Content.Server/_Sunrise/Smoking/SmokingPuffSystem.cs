using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.DoAfter;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Popups;
using Content.Shared._Sunrise.Smoking;
using Content.Shared.Actions;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Smoking;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Sunrise.Smoking;

/// <summary>
/// Система активных затяжек тлеющими сигаретами, сигарами и трубками.
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
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SmokablePuffComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<SmokablePuffComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<SmokablePuffComponent, SmokablePuffDoAfterEvent>(OnPuffDoAfter);
        SubscribeLocalEvent<SmokablePuffComponent, GetItemActionsEvent>(OnSmokableGetActions);
        SubscribeLocalEvent<SmokablePuffComponent, BlowSmokeRingActionEvent>(OnSmokableBlowSmokeRing);
        SubscribeLocalEvent<SmokablePuffComponent, SmokeRingDoAfterEvent>(OnSmokeRingDoAfter);
    }

    private void OnUseInHand(Entity<SmokablePuffComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (TryStartPuff(entity, args.User, args.User))
            args.Handled = true;
    }

    private void OnAfterInteract(Entity<SmokablePuffComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        if (TryStartPuff(entity, args.User, args.Target.Value))
            args.Handled = true;
    }

    public bool CanPuff(
        Entity<SmokablePuffComponent> entity,
        EntityUid user,
        EntityUid target,
        out SmokableComponent? smokable,
        out Entity<SolutionComponent>? targetSoln,
        out Solution? solution,
        bool quiet = false)
    {
        smokable = null;
        targetSoln = null;
        solution = null;

        if (!TryComp<SmokableComponent>(entity.Owner, out smokable))
            return false;

        // Затягиваться можно только горящей/тлеющей сигаретой
        if (smokable.State != SmokableState.Lit)
            return false;

        if (!HasComp<BloodstreamComponent>(target))
            return false;

        if (!_ingestion.HasMouthAvailable(user, target))
        {
            if (!quiet)
                _popup.PopupEntity(Loc.GetString("vape-component-mouth-blocked"), user, user);
            return false;
        }

        if (_timing.CurTime < entity.Comp.LastPuffTime + entity.Comp.Cooldown)
            return false;

        if (!_solution.TryGetSolution(entity.Owner, smokable.Solution, out targetSoln, out solution) ||
            solution.Volume <= FixedPoint2.Zero)
        {
            if (!quiet)
                _popup.PopupEntity(Loc.GetString("smoking-puff-burnt-out", ("item", entity.Owner)), user, user);
            return false;
        }

        return true;
    }

    public bool TryStartPuff(Entity<SmokablePuffComponent> entity, EntityUid user, EntityUid target)
    {
        if (!CanPuff(entity, user, target, out _, out _, out _))
            return false;

        var forced = (target != user);
        var delay = forced ? entity.Comp.ForcedPuffDelay : entity.Comp.PuffDelay;

        if (entity.Comp.PuffSound != null)
        {
            _audio.PlayPvs(entity.Comp.PuffSound, target, AudioParams.Default.WithVolume(-1.0f).WithMaxDistance(7f).WithVariation(0.04f));
        }

        if (forced)
        {
            var targetName = Identity.Entity(target, EntityManager);
            var userName = Identity.Entity(user, EntityManager);
            _popup.PopupEntity(Loc.GetString("smoking-puff-try-forced", ("user", userName)), target, target);
            _popup.PopupEntity(Loc.GetString("smoking-puff-try-user-forced", ("target", targetName)), user, user);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("smoking-puff-try"), user, user);
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, user, delay, new SmokablePuffDoAfterEvent(forced), entity.Owner, target: target, used: entity.Owner)
        {
            BreakOnMove = false,
            BreakOnDamage = true
        };

        return _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnPuffDoAfter(Entity<SmokablePuffComponent> entity, ref SmokablePuffDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target == null)
            return;

        var user = args.Args.User;
        var target = args.Args.Target.Value;

        if (!CanPuff(entity, user, target, out var smokable, out var targetSoln, out var solution, quiet: true))
            return;

        if (targetSoln == null || solution == null)
            return;

        entity.Comp.LastPuffTime = _timing.CurTime;

        var puffAmount = FixedPoint2.Min(FixedPoint2.New(entity.Comp.PuffVolume), solution.Volume);
        var inhaled = _solution.SplitSolution(targetSoln.Value, puffAmount);

        if (TryComp<BloodstreamComponent>(target, out var bloodstream))
        {
            _reactive.DoEntityReaction(target, inhaled, ReactionMethod.Ingestion);
            _bloodstream.TryAddToBloodstream((target, bloodstream), inhaled);
        }

        var environment = _atmos.GetContainingMixture(target, true, true);
        if (environment != null)
        {
            var merger = new GasMixture(1) { Temperature = smokable!.ExposeTemperature };
            merger.SetMoles(Gas.WaterVapor, 0.15f);
            _atmos.Merge(environment, merger);
        }

        if (entity.Comp.ExhaleSound != null)
        {
            _audio.PlayPvs(entity.Comp.ExhaleSound, target, AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(7f).WithVariation(0.04f));
        }

        if (args.Forced)
        {
            var targetName = Identity.Entity(target, EntityManager);
            var userName = Identity.Entity(user, EntityManager);
            _popup.PopupEntity(Loc.GetString("smoking-puff-success-forced", ("user", userName)), target, target);
            _popup.PopupEntity(Loc.GetString("smoking-puff-success-user-forced", ("target", targetName)), user, target);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("smoking-puff-success"), target, target);
        }

        // Если раствор в сигарете полностью иссяк — тушим сигарету
        if (solution.Volume <= FixedPoint2.Zero)
        {
            _smoking.SetSmokableState(entity.Owner, SmokableState.Burnt, smokable);
            _popup.PopupEntity(Loc.GetString("smoking-puff-burnt-out", ("item", entity.Owner)), user, user);
        }

        args.Handled = true;
    }

    private void OnSmokableGetActions(Entity<SmokablePuffComponent> entity, ref GetItemActionsEvent args)
    {
        if (TryComp<SmokableComponent>(entity.Owner, out var smokable) && smokable.State == SmokableState.Lit)
        {
            args.AddAction(ref entity.Comp.SmokeRingActionEntity, entity.Comp.SmokeRingAction);
        }
    }

    private void OnSmokableBlowSmokeRing(Entity<SmokablePuffComponent> entity, ref BlowSmokeRingActionEvent args)
    {
        if (args.Handled)
            return;

        if (TryStartBlowSmokeRing(entity, args.Performer, args.Target))
            args.Handled = true;
    }

    public bool TryStartBlowSmokeRing(Entity<SmokablePuffComponent> entity, EntityUid user, EntityCoordinates target)
    {
        if (!CanPuff(entity, user, user, out _, out _, out _))
            return false;

        if (entity.Comp.PuffSound != null)
        {
            _audio.PlayPvs(entity.Comp.PuffSound, user, AudioParams.Default.WithVolume(-1.0f).WithMaxDistance(7f).WithVariation(0.04f));
        }

        _popup.PopupEntity(Loc.GetString("vape-puff-ring-start"), user, user);

        var doAfterArgs = new DoAfterArgs(EntityManager, user, 0.9f, new SmokeRingDoAfterEvent(GetNetCoordinates(target)), entity.Owner, target: user, used: entity.Owner)
        {
            BreakOnMove = false,
            BreakOnDamage = true,
            NeedHand = false
        };

        return _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnSmokeRingDoAfter(Entity<SmokablePuffComponent> entity, ref SmokeRingDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.Args.User;

        if (!CanPuff(entity, user, user, out var smokable, out var targetSoln, out var solution, quiet: true))
            return;

        if (targetSoln == null || solution == null)
            return;

        entity.Comp.LastPuffTime = _timing.CurTime;

        var ringAmount = FixedPoint2.Min(FixedPoint2.New(0.2f), solution.Volume);
        var inhaled = _solution.SplitSolution(targetSoln.Value, ringAmount);

        if (TryComp<BloodstreamComponent>(user, out var bloodstream))
        {
            _reactive.DoEntityReaction(user, inhaled, ReactionMethod.Ingestion);
            _bloodstream.TryAddToBloodstream((user, bloodstream), inhaled);
        }

        var environment = _atmos.GetContainingMixture(user, true, true);
        if (environment != null)
        {
            var merger = new GasMixture(1) { Temperature = smokable!.ExposeTemperature };
            merger.SetMoles(Gas.WaterVapor, 0.15f);
            _atmos.Merge(environment, merger);
        }

        if (entity.Comp.ExhaleSound != null)
        {
            _audio.PlayPvs(entity.Comp.ExhaleSound, user, AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(7f).WithVariation(0.04f));
        }

        _popup.PopupEntity(Loc.GetString("smoking-puff-smoke-ring", ("user", Identity.Entity(user, EntityManager))), user);

        var targetCoords = GetCoordinates(args.TargetCoordinates);
        SpawnSmokeRing(user, targetCoords);

        if (solution.Volume <= FixedPoint2.Zero)
        {
            _smoking.SetSmokableState(entity.Owner, SmokableState.Burnt, smokable);
            _popup.PopupEntity(Loc.GetString("smoking-puff-burnt-out", ("item", entity.Owner)), user, user);
        }

        args.Handled = true;
    }

    private void SpawnSmokeRing(EntityUid user, EntityCoordinates? target = null)
    {
        var userXform = Transform(user);
        Vector2 dir;

        if (target != null && target.Value.IsValid(EntityManager))
        {
            var userMapCoords = _transform.GetMapCoordinates(user, userXform);
            var targetMapCoords = _transform.ToMapCoordinates(target.Value);
            if (userMapCoords.MapId == targetMapCoords.MapId)
            {
                var diff = targetMapCoords.Position - userMapCoords.Position;
                if (diff.LengthSquared() > 0.001f)
                    dir = Vector2.Normalize(diff);
                else
                    dir = userXform.LocalRotation.ToWorldVec();
            }
            else
            {
                dir = userXform.LocalRotation.ToWorldVec();
            }
        }
        else
        {
            dir = userXform.LocalRotation.ToWorldVec();
        }

        if (dir.LengthSquared() > 0.001f)
            dir = Vector2.Normalize(dir);
        else
            dir = new Vector2(0, 1);

        var spawnCoords = userXform.Coordinates.Offset(dir * 0.45f);
        var ring = Spawn("EffectSmokeRing", spawnCoords);

        if (TryComp<SmokeRingComponent>(ring, out var smokeRing))
        {
            smokeRing.Shooter = user;
            smokeRing.GasType = Gas.WaterVapor;
            if (TryComp<BuckleComponent>(user, out var buckle) && buckle.BuckledTo != null)
                smokeRing.IgnoredSeat = buckle.BuckledTo;
        }

        _throwing.TryThrow(ring, dir * 4.5f, baseThrowSpeed: 4.5f, user: user, pushbackRatio: 0f, friction: 0.02f, playSound: false, doSpin: false);
    }
}

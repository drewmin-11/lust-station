using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Buckle.Components;
using Content.Shared.Smoking;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server._Sunrise.Smoking;

/// <summary>
/// Система обработки полета, столкновений и распада колечка дыма на пар.
/// </summary>
public sealed partial class SmokeRingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SmokeRingComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SmokeRingComponent, StartCollideEvent>(OnCollide);
        SubscribeLocalEvent<SmokeRingComponent, ThrowDoHitEvent>(OnThrowHit);
        SubscribeLocalEvent<SmokeRingComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnInit(Entity<SmokeRingComponent> entity, ref ComponentInit args)
    {
        entity.Comp.SpawnTime = _timing.CurTime;
    }

    private void OnCollide(Entity<SmokeRingComponent> entity, ref StartCollideEvent args)
    {
        if (entity.Comp.Dissipated)
            return;

        // Если врезается в самого стрелка
        if (args.OtherEntity == entity.Comp.Shooter)
            return;

        // Если врезается в стул / кровать / мебель, на которой сидит стрелок
        if (args.OtherEntity == entity.Comp.IgnoredSeat)
            return;

        if (entity.Comp.Shooter != null &&
            TryComp<BuckleComponent>(entity.Comp.Shooter, out var buckle) &&
            buckle.BuckledTo == args.OtherEntity)
        {
            return;
        }

        // В первые 0.15 секунды полета игнорируем препятствия, находящиеся ближе 0.65 м от стрелка
        if (_timing.CurTime - entity.Comp.SpawnTime < TimeSpan.FromSeconds(0.15f))
        {
            if (entity.Comp.Shooter != null)
            {
                var shooterPos = Transform(entity.Comp.Shooter.Value).Coordinates;
                var otherPos = Transform(args.OtherEntity).Coordinates;
                if (shooterPos.TryDistance(EntityManager, otherPos, out var dist) && dist < 0.65f)
                    return;
            }
        }

        // Если врезается в твердое препятствие (стены, объекты, сущности)
        if (!args.OtherFixture.Hard)
            return;

        DissipateRing(entity);
    }

    private void OnThrowHit(Entity<SmokeRingComponent> entity, ref ThrowDoHitEvent args)
    {
        if (entity.Comp.Dissipated)
            return;

        if (args.Target == entity.Comp.Shooter || args.Target == entity.Comp.IgnoredSeat)
            return;

        if (entity.Comp.Shooter != null &&
            TryComp<BuckleComponent>(entity.Comp.Shooter, out var buckle) &&
            buckle.BuckledTo == args.Target)
        {
            return;
        }

        DissipateRing(entity);
    }

    private void OnShutdown(Entity<SmokeRingComponent> entity, ref ComponentShutdown args)
    {
        if (!entity.Comp.Dissipated)
            DissipateRing(entity);
    }

    /// <summary>
    /// Растворяет колечко в атмосферный газ (водяной пар или табачный дым) без спавна эффектов.
    /// </summary>
    public void DissipateRing(Entity<SmokeRingComponent> entity)
    {
        if (entity.Comp.Dissipated)
            return;

        entity.Comp.Dissipated = true;

        var environment = _atmos.GetContainingMixture(entity.Owner, true, true);
        if (environment != null)
        {
            var merger = new GasMixture(1) { Temperature = Atmospherics.T20C + 15f };
            merger.SetMoles(entity.Comp.GasType, 1.8f);
            _atmos.Merge(environment, merger);
        }

        QueueDel(entity.Owner);
    }
}

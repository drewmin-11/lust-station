using System.Numerics;
using Content.Server.DoAfter;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Nutrition.Components;
using Content.Server.Popups;
using Content.Server._Sunrise.Smoking;
using Content.Shared._Sunrise.Smoking;
using Content.Shared.Actions;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Smoking;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Nutrition.EntitySystems
{
    public sealed partial class SmokingSystem
    {
        [Dependency] private DoAfterSystem _doAfterSystem = default!;
        [Dependency] private ExplosionSystem _explosionSystem = default!;
        [Dependency] private PopupSystem _popupSystem = default!;
        [Dependency] private IngestionSystem _ingestion = default!;
        [Dependency] private ThrowingSystem _throwing = default!;
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private EmagSystem _emag = default!;

        private void InitializeVapes()
        {
            SubscribeLocalEvent<VapeComponent, MapInitEvent>(OnVapeMapInit);
            SubscribeLocalEvent<VapeComponent, UseInHandEvent>(OnVapeUseInHand);
            SubscribeLocalEvent<VapeComponent, AfterInteractEvent>(OnVapeAfterInteract);
            SubscribeLocalEvent<VapeComponent, VapeDoAfterEvent>(OnVapeDoAfter);
            SubscribeLocalEvent<VapeComponent, GotEmaggedEvent>(OnVapeEmagged);
            SubscribeLocalEvent<VapeComponent, GetItemActionsEvent>(OnVapeGetActions);
            SubscribeLocalEvent<VapeComponent, BlowSmokeRingActionEvent>(OnVapeBlowSmokeRing);
            SubscribeLocalEvent<VapeComponent, SmokeRingDoAfterEvent>(OnVapeSmokeRingDoAfter);
            SubscribeLocalEvent<VapeComponent, EntInsertedIntoContainerMessage>(OnVapeInserted);
            SubscribeLocalEvent<VapeComponent, EntRemovedFromContainerMessage>(OnVapeRemoved);
        }

        private void OnVapeMapInit(Entity<VapeComponent> entity, ref MapInitEvent args)
        {
            UpdateVapeAppearance(entity);
        }

        private void OnVapeUseInHand(Entity<VapeComponent> entity, ref UseInHandEvent args)
        {
            if (args.Handled)
                return;

            if (TryStartVape(entity, args.User, args.User))
                args.Handled = true;
        }

        private void OnVapeAfterInteract(Entity<VapeComponent> entity, ref AfterInteractEvent args)
        {
            if (args.Handled || !args.CanReach || args.Target == null)
                return;

            if (TryStartVape(entity, args.User, args.Target.Value))
                args.Handled = true;
        }

        /// <summary>
        /// Проверяет, может ли персонаж затянуться вейпом в текущих условиях.
        /// Не производит сайд-эффектов (кроме popup при quiet = false).
        /// </summary>
        public bool CanVape(
            Entity<VapeComponent> entity,
            EntityUid user,
            EntityUid target,
            out Entity<SolutionComponent>? targetSoln,
            out Solution? solution,
            bool quiet = false)
        {
            targetSoln = null;
            solution = null;

            if (!HasComp<BloodstreamComponent>(target))
                return false;

            // Проверка доступности рта (если маска закрывает рот - попап игроку)
            if (!_ingestion.HasMouthAvailable(user, target))
            {
                if (!quiet)
                    _popupSystem.PopupEntity(Loc.GetString("vape-component-mouth-blocked"), user, user);
                return false;
            }

            // Проверка кулдауна затяжки
            if (_timing.CurTime < entity.Comp.LastPuffTime + entity.Comp.Cooldown)
                return false;

            // Проверка картриджа в слоте ItemSlots
            if (TryComp<ItemSlotsComponent>(entity.Owner, out var itemSlots) &&
                _itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.CartridgeSlot, out var slot, itemSlots))
            {
                if (slot.Item == null)
                {
                    if (!quiet)
                        _popupSystem.PopupEntity(Loc.GetString("vape-component-no-cartridge"), user, user);
                    return false;
                }

                if (!_solutionContainerSystem.TryGetRefillableSolution(slot.Item.Value, out targetSoln, out solution))
                {
                    if (!quiet)
                        _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-empty"), user, user);
                    return false;
                }
            }
            else
            {
                if (!_solutionContainerSystem.TryGetRefillableSolution(entity.Owner, out targetSoln, out solution))
                    return false;
            }

            if (solution == null || solution.Volume <= FixedPoint2.Zero)
            {
                if (!quiet)
                    _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-empty"), user, user);
                return false;
            }

            return true;
        }

        public bool TryStartVape(Entity<VapeComponent> entity, EntityUid user, EntityUid target)
        {
            if (!CanVape(entity, user, target, out var targetSoln, out var solution))
                return false;

            var forced = (target != user);
            var delay = forced ? entity.Comp.Delay : entity.Comp.UserDelay;

            // Проверка на взрыв от реагентов или емага
            if (entity.Comp.ExplodeOnUse || _emag.CheckFlag(entity, EmagType.Interaction))
            {
                _explosionSystem.QueueExplosion(entity.Owner, "Default", entity.Comp.ExplosionIntensity, 0.5f, 3, canCreateVacuum: false);
                QueueDel(entity.Owner);
                return false;
            }

            foreach (var reagent in solution!.Contents)
            {
                var id = reagent.Reagent.Prototype;
                if (id == "Plasma" || id == "Tritium" || id == "Nitroglycerin")
                {
                    _explosionSystem.QueueExplosion(entity.Owner, "Default", entity.Comp.ExplosionIntensity, 0.5f, 3, canCreateVacuum: false);
                    QueueDel(entity.Owner);
                    return false;
                }
            }

            if (forced)
            {
                var targetName = Identity.Entity(target, EntityManager);
                var userName = Identity.Entity(user, EntityManager);

                _popupSystem.PopupEntity(Loc.GetString("vape-component-try-use-vape-forced", ("user", userName)), target, target);
                _popupSystem.PopupEntity(Loc.GetString("vape-component-try-use-vape-forced-user", ("target", targetName)), user, user);
            }
            else
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-try-use-vape"), user, user);
            }

            // Звук затяжки
            if (entity.Comp.PuffSound != null)
            {
                _audio.PlayPvs(entity.Comp.PuffSound, target, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.0f).WithMaxDistance(7f).WithVariation(0.04f));
            }

            var vapeDoAfterEvent = new VapeDoAfterEvent(solution, forced);
            _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, user, delay, vapeDoAfterEvent, entity.Owner, target: target, used: entity.Owner)
            {
                BreakOnMove = false,
                BreakOnDamage = true
            });

            return true;
        }

        private void OnVapeDoAfter(Entity<VapeComponent> entity, ref VapeDoAfterEvent args)
        {
            if (args.Cancelled || args.Handled || args.Args.Target == null)
                return;

            var user = args.Args.User;
            var target = args.Args.Target.Value;

            if (!CanVape(entity, user, target, out var targetSoln, out var solution, quiet: true))
                return;

            if (targetSoln == null || solution == null)
                return;

            entity.Comp.LastPuffTime = _timing.CurTime;

            var puffAmount = FixedPoint2.Min(FixedPoint2.New(entity.Comp.PuffVolume), solution.Volume);
            var inhaled = _solutionContainerSystem.SplitSolution(targetSoln.Value, puffAmount);

            if (TryComp<BloodstreamComponent>(target, out var bloodstream))
            {
                _reactiveSystem.DoEntityReaction(target, inhaled, ReactionMethod.Ingestion);
                _bloodstreamSystem.TryAddToBloodstream((target, bloodstream), inhaled);
            }

            // Выделяем стандартный водяной пар в атмосферу
            var environment = _atmos.GetContainingMixture(target, true, true);
            if (environment != null)
            {
                var merger = new GasMixture(1) { Temperature = solution.Temperature };
                merger.SetMoles(Gas.WaterVapor, entity.Comp.VaporMolesPerPuff);
                _atmos.Merge(environment, merger);
            }

            if (entity.Comp.ExhaleSound != null)
            {
                _audio.PlayPvs(entity.Comp.ExhaleSound, target, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(7f).WithVariation(0.04f));
            }

            if (args.Forced)
            {
                var targetName = Identity.Entity(target, EntityManager);
                var userName = Identity.Entity(user, EntityManager);

                _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-success-forced", ("user", userName)), target, target);
                _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-success-user-forced", ("target", targetName)), user, target);
            }
            else
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-success"), target, target);
            }

            args.Handled = true;
        }

        private void OnVapeGetActions(Entity<VapeComponent> entity, ref GetItemActionsEvent args)
        {
            args.AddAction(ref entity.Comp.SmokeRingActionEntity, entity.Comp.SmokeRingAction);
        }

        private void OnVapeBlowSmokeRing(Entity<VapeComponent> entity, ref BlowSmokeRingActionEvent args)
        {
            if (args.Handled)
                return;

            if (TryStartBlowVapeSmokeRing(entity, args.Performer, args.Target))
                args.Handled = true;
        }

        public bool TryStartBlowVapeSmokeRing(Entity<VapeComponent> entity, EntityUid user, EntityCoordinates target)
        {
            if (!CanVape(entity, user, user, out _, out _))
                return false;

            if (entity.Comp.PuffSound != null)
            {
                _audio.PlayPvs(entity.Comp.PuffSound, user, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.0f).WithMaxDistance(7f).WithVariation(0.04f));
            }

            _popupSystem.PopupEntity(Loc.GetString("vape-puff-ring-start"), user, user);

            var doAfterArgs = new DoAfterArgs(EntityManager, user, 0.8f, new SmokeRingDoAfterEvent(GetNetCoordinates(target)), entity.Owner, target: user, used: entity.Owner)
            {
                BreakOnMove = false,
                BreakOnDamage = true,
                NeedHand = false
            };

            return _doAfterSystem.TryStartDoAfter(doAfterArgs);
        }

        private void OnVapeSmokeRingDoAfter(Entity<VapeComponent> entity, ref SmokeRingDoAfterEvent args)
        {
            if (args.Cancelled || args.Handled)
                return;

            var user = args.Args.User;

            if (!CanVape(entity, user, user, out var targetSoln, out var solution, quiet: true))
                return;

            if (targetSoln == null || solution == null)
                return;

            entity.Comp.LastPuffTime = _timing.CurTime;

            var ringAmount = FixedPoint2.Min(FixedPoint2.New(0.5f), solution.Volume);
            var inhaled = _solutionContainerSystem.SplitSolution(targetSoln.Value, ringAmount);

            if (TryComp<BloodstreamComponent>(user, out var bloodstream))
            {
                _reactiveSystem.DoEntityReaction(user, inhaled, ReactionMethod.Ingestion);
                _bloodstreamSystem.TryAddToBloodstream((user, bloodstream), inhaled);
            }

            var environment = _atmos.GetContainingMixture(user, true, true);
            if (environment != null)
            {
                var merger = new GasMixture(1) { Temperature = solution.Temperature };
                merger.SetMoles(Gas.WaterVapor, 0.5f);
                _atmos.Merge(environment, merger);
            }

            if (entity.Comp.ExhaleSound != null)
            {
                _audio.PlayPvs(entity.Comp.ExhaleSound, user, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(7f).WithVariation(0.04f));
            }

            _popupSystem.PopupEntity(Loc.GetString("smoking-puff-smoke-ring", ("user", Identity.Entity(user, EntityManager))), user);

            var targetCoords = GetCoordinates(args.TargetCoordinates);
            SpawnSmokeRing(user, targetCoords);

            args.Handled = true;
        }

        private void SpawnSmokeRing(EntityUid user, EntityCoordinates? target = null)
        {
            var userXform = Transform(user);
            Vector2 dir;

            if (target != null && target.Value.IsValid(EntityManager))
            {
                var userMapCoords = _transformSystem.GetMapCoordinates(user, userXform);
                var targetMapCoords = _transformSystem.ToMapCoordinates(target.Value);
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

        private void OnVapeInserted(Entity<VapeComponent> entity, ref EntInsertedIntoContainerMessage args)
        {
            if (args.Container.ID == entity.Comp.CartridgeSlot)
            {
                UpdateVapeAppearance(entity);
            }
        }

        private void OnVapeRemoved(Entity<VapeComponent> entity, ref EntRemovedFromContainerMessage args)
        {
            if (args.Container.ID == entity.Comp.CartridgeSlot)
            {
                UpdateVapeAppearance(entity);
            }
        }

        /// <summary>
        /// Обновляет визуал вейпа (наличие картриджа) без хардкода спрайтов.
        /// </summary>
        public void UpdateVapeAppearance(Entity<VapeComponent> entity)
        {
            var hasCartridge = false;

            if (TryComp<ItemSlotsComponent>(entity.Owner, out var itemSlots) &&
                _itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.CartridgeSlot, out var slot, itemSlots))
            {
                hasCartridge = slot.Item != null;
            }

            _appearance.SetData(entity.Owner, VapeVisuals.HasCartridge, hasCartridge);
        }

        private void OnVapeEmagged(Entity<VapeComponent> entity, ref GotEmaggedEvent args)
        {
            if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
                return;

            if (_emag.CheckFlag(entity, EmagType.Interaction))
                return;

            args.Handled = true;
        }
    }
}

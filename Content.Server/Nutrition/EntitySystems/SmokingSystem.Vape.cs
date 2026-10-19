using System.Numerics;
using Content.Server.DoAfter;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Nutrition.Components;
using Content.Server.Popups;
using Content.Server._Sunrise.Smoking;
using Content.Shared._Sunrise.Smoking;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;

using Content.Shared.Nutrition;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Smoking;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

/// <summary>
/// System for vapes
/// </summary>
namespace Content.Server.Nutrition.EntitySystems
{
    public sealed partial class SmokingSystem
    {
        private static readonly ProtoId<TagPrototype> VapeCartridgeTag = "VapeCartridge";

        [Dependency] private DoAfterSystem _doAfterSystem = default!;
        [Dependency] private EmagSystem _emag = default!;
        [Dependency] private IngestionSystem _ingestion = default!;
        [Dependency] private ExplosionSystem _explosionSystem = default!;
        [Dependency] private PopupSystem _popupSystem = default!;
        // Sunrise-Start
        [Dependency] private Content.Server._Sunrise.Smoking.NicotineAddictionSystem _nicotine = default!;
        [Dependency] private ThrowingSystem _throwing = default!;
        [Dependency] private TagSystem _tagSystem = default!;
        [Dependency] private IPrototypeManager _protoManager = default!;
        // Sunrise-End

        private void InitializeVapes()
        {
            SubscribeLocalEvent<VapeComponent, MapInitEvent>(OnVapeMapInit);
            SubscribeLocalEvent<VapeComponent, AfterInteractEvent>(OnVapeInteraction);
            SubscribeLocalEvent<VapeComponent, UseInHandEvent>(OnVapeUseInHand);
            SubscribeLocalEvent<VapeComponent, VapeDoAfterEvent>(OnVapeDoAfter);
            SubscribeLocalEvent<VapeComponent, GotEmaggedEvent>(OnEmagged);
            SubscribeLocalEvent<VapeComponent, EntInsertedIntoContainerMessage>(OnVapeInserted);
            SubscribeLocalEvent<VapeComponent, EntRemovedFromContainerMessage>(OnVapeRemoved);
            SubscribeLocalEvent<VapeComponent, GetItemActionsEvent>(OnVapeGetActions);
            SubscribeLocalEvent<VapeComponent, BlowSmokeRingActionEvent>(OnVapeBlowSmokeRing);
            SubscribeLocalEvent<VapeComponent, SmokeRingDoAfterEvent>(OnVapeSmokeRingDoAfter);
            SubscribeLocalEvent<VapeComponent, InteractUsingEvent>(OnVapeInteractUsing);
            SubscribeLocalEvent<VapeCartridgeComponent, InteractUsingEvent>(OnCartridgeInteractUsing);
            SubscribeLocalEvent<VapeCartridgeComponent, SolutionChangedEvent>(OnCartridgeSolutionChanged);
            SubscribeLocalEvent<VapeCartridgeComponent, MapInitEvent>(OnCartridgeMapInit);
        }

        private void OnVapeUseInHand(Entity<VapeComponent> entity, ref UseInHandEvent args)
        {
            if (args.Handled)
                return;

            if (TryStartVape(entity, args.User, args.User))
                args.Handled = true;
        }

        private void OnVapeInteraction(Entity<VapeComponent> entity, ref AfterInteractEvent args)
        {
            if (args.Handled || !args.CanReach || args.Target == null)
                return;

            if (TryStartVape(entity, args.User, args.Target.Value))
                args.Handled = true;
        }

        public bool TryStartVape(Entity<VapeComponent> entity, EntityUid user, EntityUid target)
        {
            if (!HasComp<BloodstreamComponent>(target))
                return false;

            if (!_ingestion.HasMouthAvailable(user, target))
            {
                _popupSystem.PopupEntity(Loc.GetString("smoking-puff-helmet-blocked"), user, user);
                return false;
            }

            // Sunrise edit start - поддержка картриджей и нормальной заправки вейпа
            Solution? solution = null;

            if (TryComp<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(entity.Owner, out var itemSlots) &&
                _itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.CartridgeSlot, out var slot, itemSlots))
            {
                if (slot.Item == null)
                {
                    _popupSystem.PopupEntity(Loc.GetString("vape-component-no-cartridge"), user, user);
                    return false;
                }

                if (!_solutionContainerSystem.TryGetRefillableSolution(slot.Item.Value, out _, out solution))
                {
                    _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-empty"), user, user);
                    return false;
                }
            }
            else
            {
                if (!_solutionContainerSystem.TryGetRefillableSolution(entity.Owner, out _, out solution))
                    return false;
            }

            if (solution == null || solution.Contents.Count == 0 || solution.Volume <= Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-empty"), target, user);
                return false;
            }

            var forced = (target != user);
            var delay = forced ? entity.Comp.Delay : entity.Comp.UserDelay;

            if (entity.Comp.ExplodeOnUse || _emag.CheckFlag(entity, EmagType.Interaction))
            {
                _explosionSystem.QueueExplosion(entity.Owner, "Default", entity.Comp.ExplosionIntensity, 0.5f, 3, canCreateVacuum: false);
                Del(entity);
                return false;
            }

            // Взрываем вейп только при наличии опасных нестабильных реагентов (плазма, тритий)
            foreach (var name in solution.Contents)
            {
                var id = name.Reagent.Prototype;
                if (id == "Plasma" || id == "Tritium" || id == "Nitroglycerin")
                {
                    _explosionSystem.QueueExplosion(entity.Owner, "Default", entity.Comp.ExplosionIntensity, 0.5f, 3, canCreateVacuum: false);
                    Del(entity);
                    return false;
                }
            }

            if (forced)
            {
                var targetName = Identity.Entity(target, EntityManager);
                var userName = Identity.Entity(user, EntityManager);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape-forced", ("user", userName)), target, target);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape-forced-user", ("target", targetName)), user, user);
            }
            else
            {
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape"), user, user);
            }

            // Sunrise-Start: проигрываем звук затяжки прямо от персонажа
            if (entity.Comp.PuffSound != null)
            {
                _audio.PlayPvs(entity.Comp.PuffSound, target, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.0f).WithMaxDistance(7f).WithVariation(0.04f));
            }
            // Sunrise-End

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

            var environment = _atmos.GetContainingMixture(args.Args.Target.Value, true, true);
            if (environment == null)
            {
                return;
            }

            Entity<SolutionComponent>? targetSoln = null;
            Solution? solution = null;
            EntityUid? cartridgeUid = null;

            if (TryComp<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(entity.Owner, out var itemSlots) &&
                _itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.CartridgeSlot, out var slot, itemSlots))
            {
                if (slot.Item != null && _solutionContainerSystem.TryGetRefillableSolution(slot.Item.Value, out targetSoln, out solution))
                {
                    cartridgeUid = slot.Item.Value;
                }
            }
            else
            {
                _solutionContainerSystem.TryGetRefillableSolution(entity.Owner, out targetSoln, out solution);
            }

            if (targetSoln == null || solution == null || solution.Volume <= Content.Shared.FixedPoint.FixedPoint2.Zero)
                return;

            // Sunrise edit start - постепенный расход жидкости, никотиновый эффект и густой цветной пар
            var puffAmount = Content.Shared.FixedPoint.FixedPoint2.Min(Content.Shared.FixedPoint.FixedPoint2.New(entity.Comp.PuffVolume), solution.Volume);
            var vaporColor = solution.GetColor(_protoManager).WithAlpha(0.85f);
            var inhaled = _solutionContainerSystem.SplitSolution(targetSoln.Value, puffAmount);

            if (cartridgeUid != null)
            {
                UpdateCartridgeAppearance(cartridgeUid.Value);
                UpdateVapeAppearance(entity);
            }

            // Если раствор содержал никотин — утоляем зависимость
            if (inhaled.GetTotalPrototypeQuantity("Nicotine") > Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                _nicotine.RegisterPuff(args.Args.Target.Value);
            }

            if (TryComp<BloodstreamComponent>(args.Args.Target.Value, out var bloodstream))
            {
                _reactiveSystem.DoEntityReaction(args.Args.Target.Value, inhaled, ReactionMethod.Ingestion);
                _bloodstreamSystem.TryAddToBloodstream((args.Args.Target.Value, bloodstream), inhaled);
            }

            var merger = new GasMixture(1) { Temperature = solution.Temperature };
            // Выделяем стандартный водяной пар в атмосферу в достаточном количестве, чтобы он был отчетливо виден
            merger.SetMoles(entity.Comp.GasType, 2.5f);
            _atmos.Merge(environment, merger);

            if (entity.Comp.ExhaleSound != null)
            {
                _audio.PlayPvs(entity.Comp.ExhaleSound, args.Args.Target.Value, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(7f).WithVariation(0.04f));
            }
            // Sunrise edit end

            if (args.Forced)
            {
                var targetName = Identity.Entity(args.Args.Target.Value, EntityManager);
                var userName = Identity.Entity(args.Args.User, EntityManager);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success-forced", ("user", userName)), args.Args.Target.Value,
                    args.Args.Target.Value);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success-user-forced", ("target", targetName)), args.Args.User,
                    args.Args.Target.Value);
            }
            else
            {
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success"), args.Args.Target.Value,
                    args.Args.Target.Value);
            }
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

        private void OnVapeInteractUsing(Entity<VapeComponent> entity, ref InteractUsingEvent args)
        {
            if (args.Handled)
                return;

            if (!TryComp<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(entity.Owner, out var itemSlots) ||
                !_itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.CartridgeSlot, out var slot, itemSlots))
            {
                return;
            }

            // 1. Если кликают картриджем в руках по вейпу - вставляем картридж в слот по ЛКМ
            if (HasComp<VapeCartridgeComponent>(args.Used) ||
                (TryComp<TagComponent>(args.Used, out var tag) && _tagSystem.HasTag(tag, VapeCartridgeTag)))
            {
                if (slot.Item == null)
                {
                    if (_itemSlotsSystem.TryInsertFromHand(entity.Owner, slot, args.User) ||
                        _itemSlotsSystem.TryInsert(entity.Owner, slot, args.Used, args.User))
                    {
                        UpdateCartridgeAppearance(args.Used);
                        UpdateVapeAppearance(entity);
                        args.Handled = true;
                        return;
                    }
                }
                return;
            }

            // 2. Если кликают банкой жижи или другим контейнером с жидкостью по вейпу
            if (!_solutionContainerSystem.TryGetDrainableSolution(args.Used, out var drainableSoln, out var drainableSolution))
                return;

            if (slot.Item == null)
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-no-cartridge"), entity.Owner, args.User);
                args.Handled = true;
                return;
            }

            if (!_solutionContainerSystem.TryGetRefillableSolution(slot.Item.Value, out var targetSoln, out var targetSolution))
                return;

            if (targetSolution.AvailableVolume <= Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-cartridge-full"), entity.Owner, args.User);
                args.Handled = true;
                return;
            }

            if (drainableSolution.Volume <= Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-juice-empty"), entity.Owner, args.User);
                args.Handled = true;
                return;
            }

            var transferAmount = Content.Shared.FixedPoint.FixedPoint2.Min(Content.Shared.FixedPoint.FixedPoint2.New(5), targetSolution.AvailableVolume);
            transferAmount = Content.Shared.FixedPoint.FixedPoint2.Min(transferAmount, drainableSolution.Volume);

            if (transferAmount <= Content.Shared.FixedPoint.FixedPoint2.Zero)
                return;

            var transferred = _solutionContainerSystem.Drain(args.Used, drainableSoln.Value, transferAmount);
            _solutionContainerSystem.Refill(slot.Item.Value, targetSoln.Value, transferred);

            UpdateCartridgeAppearance(slot.Item.Value);
            UpdateVapeAppearance(entity);

            _popupSystem.PopupEntity(Loc.GetString("vape-component-cartridge-refilled", ("amount", transferAmount)), entity.Owner, args.User);
            _audio.PlayPvs(new Robust.Shared.Audio.SoundPathSpecifier("/Audio/Effects/Chemistry/bubbles.ogg"), entity.Owner);
            args.Handled = true;
        }

        /// <summary>
        /// Прямая заправка картриджа жидкостью из флакона при клике флаконом по картриджу в руках или в мире.
        /// </summary>
        private void OnCartridgeInteractUsing(Entity<VapeCartridgeComponent> entity, ref InteractUsingEvent args)
        {
            if (args.Handled)
                return;

            if (!_solutionContainerSystem.TryGetDrainableSolution(args.Used, out var drainableSoln, out var drainableSolution))
                return;

            if (!_solutionContainerSystem.TryGetRefillableSolution(entity.Owner, out var targetSoln, out var targetSolution))
                return;

            if (targetSolution.AvailableVolume <= Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-cartridge-full"), entity.Owner, args.User);
                args.Handled = true;
                return;
            }

            if (drainableSolution.Volume <= Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-juice-empty"), entity.Owner, args.User);
                args.Handled = true;
                return;
            }

            var transferAmount = Content.Shared.FixedPoint.FixedPoint2.Min(Content.Shared.FixedPoint.FixedPoint2.New(5), targetSolution.AvailableVolume);
            transferAmount = Content.Shared.FixedPoint.FixedPoint2.Min(transferAmount, drainableSolution.Volume);

            if (transferAmount <= Content.Shared.FixedPoint.FixedPoint2.Zero)
                return;

            var transferred = _solutionContainerSystem.Drain(args.Used, drainableSoln.Value, transferAmount);
            _solutionContainerSystem.Refill(entity.Owner, targetSoln.Value, transferred);

            UpdateCartridgeAppearance(entity.Owner);

            _popupSystem.PopupEntity(Loc.GetString("vape-component-cartridge-refilled", ("amount", transferAmount)), entity.Owner, args.User);
            _audio.PlayPvs(new Robust.Shared.Audio.SoundPathSpecifier("/Audio/Effects/Chemistry/bubbles.ogg"), entity.Owner);
            args.Handled = true;
        }

        /// <summary>
        /// Запуск DoAfter затяжки перед выдуванием колечка пара из вейпа в точку прицела.
        /// </summary>
        public bool TryStartBlowVapeSmokeRing(Entity<VapeComponent> entity, EntityUid user, EntityCoordinates target)
        {
            if (!HasComp<BloodstreamComponent>(user))
                return false;

            if (!_ingestion.HasMouthAvailable(user, user))
            {
                _popupSystem.PopupEntity(Loc.GetString("smoking-puff-helmet-blocked"), user, user);
                return false;
            }

            Solution? solution = null;

            if (TryComp<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(entity.Owner, out var itemSlots) &&
                _itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.CartridgeSlot, out var slot, itemSlots))
            {
                if (slot.Item == null)
                {
                    _popupSystem.PopupEntity(Loc.GetString("vape-component-no-cartridge"), user, user);
                    return false;
                }

                if (!_solutionContainerSystem.TryGetRefillableSolution(slot.Item.Value, out _, out solution))
                {
                    _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-empty"), user, user);
                    return false;
                }
            }
            else
            {
                if (!_solutionContainerSystem.TryGetRefillableSolution(entity.Owner, out _, out solution))
                    return false;
            }

            if (solution == null || solution.Contents.Count == 0 || solution.Volume <= Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                _popupSystem.PopupEntity(Loc.GetString("vape-component-vape-empty"), user, user);
                return false;
            }

            // Звук затяжки вейпом при начале DoAfter
            if (entity.Comp.PuffSound != null)
            {
                _audio.PlayPvs(entity.Comp.PuffSound, user, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.0f).WithMaxDistance(7f).WithVariation(0.04f));
            }

            _popupSystem.PopupEntity(Loc.GetString("vape-puff-ring-start"), user, user);

            var doAfterArgs = new DoAfterArgs(EntityManager, user, 0.9f, new SmokeRingDoAfterEvent(GetNetCoordinates(target)), entity.Owner, target: user, used: entity.Owner)
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

            if (!HasComp<BloodstreamComponent>(user) || !_ingestion.HasMouthAvailable(user, user))
                return;

            Entity<SolutionComponent>? targetSoln = null;
            Solution? solution = null;
            EntityUid? cartridgeUid = null;

            if (TryComp<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(entity.Owner, out var itemSlots) &&
                _itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.CartridgeSlot, out var slot, itemSlots))
            {
                if (slot.Item != null && _solutionContainerSystem.TryGetRefillableSolution(slot.Item.Value, out targetSoln, out solution))
                {
                    cartridgeUid = slot.Item.Value;
                }
            }
            else
            {
                _solutionContainerSystem.TryGetRefillableSolution(entity.Owner, out targetSoln, out solution);
            }

            if (targetSoln == null || solution == null || solution.Volume <= Content.Shared.FixedPoint.FixedPoint2.Zero)
                return;

            var ringAmount = Content.Shared.FixedPoint.FixedPoint2.Min(Content.Shared.FixedPoint.FixedPoint2.New(0.5f), solution.Volume);
            var vaporColor = solution.GetColor(_protoManager).WithAlpha(0.85f);
            var inhaled = _solutionContainerSystem.SplitSolution(targetSoln.Value, ringAmount);

            if (cartridgeUid != null)
            {
                UpdateCartridgeAppearance(cartridgeUid.Value);
                UpdateVapeAppearance(entity);
            }

            if (inhaled.GetTotalPrototypeQuantity("Nicotine") > Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                _nicotine.RegisterPuff(user);
            }

            if (TryComp<BloodstreamComponent>(user, out var bloodstream))
            {
                _reactiveSystem.DoEntityReaction(user, inhaled, ReactionMethod.Ingestion);
                _bloodstreamSystem.TryAddToBloodstream((user, bloodstream), inhaled);
            }

            var environment = _atmos.GetContainingMixture(user, true, true);
            if (environment != null)
            {
                var merger = new GasMixture(1) { Temperature = solution.Temperature };
                merger.SetMoles(entity.Comp.GasType, 0.5f);
                _atmos.Merge(environment, merger);
            }

            if (entity.Comp.ExhaleSound != null)
            {
                _audio.PlayPvs(entity.Comp.ExhaleSound, user, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(7f).WithVariation(0.04f));
            }

            _popupSystem.PopupEntity(
                Loc.GetString("smoking-puff-smoke-ring", ("user", Identity.Entity(user, EntityManager))),
                user);

            var targetCoords = GetCoordinates(args.TargetCoordinates);
            SpawnSmokeRing(user, targetCoords, Color.White.WithAlpha(0.95f));

            args.Handled = true;
        }

        /// <summary>
        /// Создает и запускает летящее колечко пара в направлении клика курсора или взгляда персонажа.
        /// </summary>
        private void SpawnSmokeRing(EntityUid user, EntityCoordinates? target = null, Color? vaporColor = null)
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

            // Спавним колечко чуть спереди персонажа (изо рта), чтобы не задевать стул под собой
            var spawnCoords = userXform.Coordinates.Offset(dir * 0.45f);
            var ring = Spawn("EffectSmokeRing", spawnCoords);

            var ringColor = vaporColor ?? Color.White;
            _appearance.SetData(ring, SmokeVisuals.Color, ringColor);

            if (TryComp<SmokeRingComponent>(ring, out var smokeRing))
            {
                smokeRing.Shooter = user;
                smokeRing.VaporColor = ringColor;
                smokeRing.GasType = Gas.WaterVapor;
                if (TryComp<BuckleComponent>(user, out var buckle) && buckle.BuckledTo != null)
                    smokeRing.IgnoredSeat = buckle.BuckledTo;
            }

            _throwing.TryThrow(ring, dir * 4.5f, baseThrowSpeed: 4.5f, user: user, pushbackRatio: 0f, friction: 0.02f, playSound: false, doSpin: false);
        }

        private void OnVapeMapInit(Entity<VapeComponent> entity, ref MapInitEvent args)
        {
            UpdateVapeAppearance(entity);
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

        private void OnCartridgeMapInit(Entity<VapeCartridgeComponent> entity, ref MapInitEvent args)
        {
            UpdateCartridgeAppearance(entity.Owner);
        }

        private void OnCartridgeSolutionChanged(Entity<VapeCartridgeComponent> entity, ref SolutionChangedEvent args)
        {
            UpdateCartridgeAppearance(entity.Owner);
        }

        /// <summary>
        /// Обновляет внешний вид картриджа в зависимости от залитого в него раствора.
        /// </summary>
        public void UpdateCartridgeAppearance(EntityUid cartridge)
        {
            string state = "empty";

            if (_solutionContainerSystem.TryGetRefillableSolution(cartridge, out _, out var solution) &&
                solution.Volume > Content.Shared.FixedPoint.FixedPoint2.Zero)
            {
                if (solution.GetTotalPrototypeQuantity("JuiceLime") > Content.Shared.FixedPoint.FixedPoint2.Zero)
                    state = "vape-mint";
                else if (solution.GetTotalPrototypeQuantity("JuiceBerry") > Content.Shared.FixedPoint.FixedPoint2.Zero)
                    state = "vape-berry";
                else if (solution.GetTotalPrototypeQuantity("Coffee") > Content.Shared.FixedPoint.FixedPoint2.Zero)
                    state = "vape-tobacco";
                else if (solution.GetTotalPrototypeQuantity("JuiceBanana") > Content.Shared.FixedPoint.FixedPoint2.Zero)
                    state = "vape-banana";
                else
                    state = "vape-mint";
            }
            else
            {
                state = "empty";
            }

            _appearance.SetData(cartridge, SmokingVisuals.CartridgeState, state);

            // Если картридж вставлен в вейп, обновляем и вейп
            if (_container.TryGetContainingContainer(cartridge, out var container) &&
                TryComp<VapeComponent>(container.Owner, out var vape))
            {
                UpdateVapeAppearance((container.Owner, vape));
            }
        }

        /// <summary>
        /// Обновляет внешний вид вейпа в зависимости от вставленного картриджа и его жидкости.
        /// </summary>
        public void UpdateVapeAppearance(Entity<VapeComponent> entity)
        {
            if (!TryComp<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(entity.Owner, out var itemSlots) ||
                !_itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.CartridgeSlot, out var slot, itemSlots))
            {
                return;
            }

            string state = "empty";

            if (slot.Item != null)
            {
                if (_solutionContainerSystem.TryGetRefillableSolution(slot.Item.Value, out _, out var solution) &&
                    solution.Volume > Content.Shared.FixedPoint.FixedPoint2.Zero)
                {
                    if (solution.GetTotalPrototypeQuantity("JuiceLime") > Content.Shared.FixedPoint.FixedPoint2.Zero)
                        state = "vape-mint";
                    else if (solution.GetTotalPrototypeQuantity("JuiceBerry") > Content.Shared.FixedPoint.FixedPoint2.Zero)
                        state = "vape-berry";
                    else if (solution.GetTotalPrototypeQuantity("Coffee") > Content.Shared.FixedPoint.FixedPoint2.Zero)
                        state = "vape-tobacco";
                    else if (solution.GetTotalPrototypeQuantity("JuiceBanana") > Content.Shared.FixedPoint.FixedPoint2.Zero)
                        state = "vape-banana";
                    else
                        state = "vape-mint";
                }
                else
                {
                    state = "icon";
                }
            }

            _appearance.SetData(entity.Owner, SmokingVisuals.CartridgeState, state);
        }

        private void OnEmagged(Entity<VapeComponent> entity, ref GotEmaggedEvent args)
        {
            if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
                return;

            if (_emag.CheckFlag(entity, EmagType.Interaction))
                return;

            args.Handled = true;
        }
    }
}

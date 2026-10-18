using Content.Server.DoAfter;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Nutrition.Components;
using Content.Server.Popups;
using Content.Shared._Sunrise.Smoking;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
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
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Utility;

/// <summary>
/// System for vapes
/// </summary>
namespace Content.Server.Nutrition.EntitySystems
{
    public sealed partial class SmokingSystem
    {
        [Dependency] private DoAfterSystem _doAfterSystem = default!;
        [Dependency] private DamageableSystem _damageableSystem = default!;
        [Dependency] private EmagSystem _emag = default!;
        [Dependency] private IngestionSystem _ingestion = default!;
        [Dependency] private ExplosionSystem _explosionSystem = default!;
        [Dependency] private PopupSystem _popupSystem = default!;
        // Sunrise-Start
        [Dependency] private Content.Server._Sunrise.Smoking.NicotineAddictionSystem _nicotine = default!;
        [Dependency] private IRobustRandom _random = default!;
        [Dependency] private ThrowingSystem _throwing = default!;
        [Dependency] private ActionContainerSystem _actionContainer = default!;
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
            SubscribeLocalEvent<VapeComponent, GetVerbsEvent<AlternativeVerb>>(AddVapeSmokeRingVerb);
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

            // Sunrise edit start - постепенный расход жидкости, никотиновый эффект и густой пар
            var puffAmount = Content.Shared.FixedPoint.FixedPoint2.Min(Content.Shared.FixedPoint.FixedPoint2.New(entity.Comp.PuffVolume), args.Solution.Volume);
            var inhaled = args.Solution.SplitSolution(puffAmount);

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

            var merger = new GasMixture(1) { Temperature = args.Solution.Temperature };
            // Выделяем густой клуб пара
            merger.SetMoles(entity.Comp.GasType, entity.Comp.VaporMolesPerPuff);

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

            if (TryBlowSmokeRing(entity, args.Performer))
                args.Handled = true;
        }

        private void AddVapeSmokeRingVerb(Entity<VapeComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanInteract || !args.CanAccess)
                return;

            var user = args.User;
            var verb = new AlternativeVerb
            {
                Text = Loc.GetString("smoking-verb-blow-smoke-ring"),
                Icon = new SpriteSpecifier.Rsi(new ResPath("/Textures/_Sunrise/Effects/smoke_ring.rsi"), "ring"),
                Act = () => TryBlowSmokeRing(entity, user),
                Priority = 2
            };
            args.Verbs.Add(verb);
        }

        /// <summary>
        /// Запуск действия выдувания колечка пара из вейпа.
        /// </summary>
        public bool TryBlowSmokeRing(Entity<VapeComponent> entity, EntityUid user)
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

            // Небольшой расход жидкости на красивое колечко
            var ringAmount = Content.Shared.FixedPoint.FixedPoint2.Min(Content.Shared.FixedPoint.FixedPoint2.New(0.5f), solution.Volume);
            var inhaled = solution.SplitSolution(ringAmount);

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
                merger.SetMoles(entity.Comp.GasType, 1.5f);
                _atmos.Merge(environment, merger);
            }

            if (entity.Comp.ExhaleSound != null)
            {
                _audio.PlayPvs(entity.Comp.ExhaleSound, user, Robust.Shared.Audio.AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(7f).WithVariation(0.04f));
            }

            _popupSystem.PopupEntity(
                Loc.GetString("smoking-puff-smoke-ring", ("user", Identity.Entity(user, EntityManager))),
                user);

            SpawnSmokeRing(user);
            return true;
        }

        /// <summary>
        /// Создает и запускает летящее колечко пара в направлении взгляда персонажа.
        /// </summary>
        private void SpawnSmokeRing(EntityUid user)
        {
            var userXform = Transform(user);
            var ring = Spawn("EffectSmokeRing", userXform.Coordinates);
            var dir = userXform.LocalRotation.ToWorldVec();
            if (dir.LengthSquared() > 0.001f)
            {
                dir = System.Numerics.Vector2.Normalize(dir);
                _throwing.TryThrow(ring, dir * 3.5f, baseThrowSpeed: 3.5f, user: user, pushbackRatio: 0f, friction: 0.02f, playSound: false, doSpin: false);
            }
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

        /// <summary>
        /// Обновляет внешний вид вейпа в зависимости от вставленного картриджа.
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
                var meta = MetaData(slot.Item.Value);
                var protoId = meta.EntityPrototype?.ID;
                state = protoId switch
                {
                    "VapeCartridgeMint" => "vape-mint",
                    "VapeCartridgeBerry" => "vape-berry",
                    "VapeCartridgeTobacco" => "vape-tobacco",
                    _ => "icon"
                };
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

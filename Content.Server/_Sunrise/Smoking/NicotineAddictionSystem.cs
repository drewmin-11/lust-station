using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Damage.Systems;
using Content.Server.DoAfter;
using Content.Server.Popups;
using Content.Shared._Sunrise.Mood;
using Content.Shared._Sunrise.Smoking;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Sunrise.Smoking;

/// <summary>
/// Система никотиновой зависимости, ломки, пластырей и лечения медикаментами.
/// </summary>
public sealed partial class NicotineAddictionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedJitteringSystem _jittering = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private ReactiveSystem _reactive = default!;

    private TimeSpan _nextUpdate = TimeSpan.Zero;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(3);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NicotinePatchItemComponent, AfterInteractEvent>(OnPatchInteract);
        SubscribeLocalEvent<NicotinePatchItemComponent, NicotinePatchDoAfterEvent>(OnPatchDoAfter);
    }

    private void OnPatchInteract(Entity<NicotinePatchItemComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        if (!HasComp<BloodstreamComponent>(args.Target.Value))
            return;

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, entity.Comp.UseTime, new NicotinePatchDoAfterEvent(), entity.Owner, target: args.Target, used: entity.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true
        });

        args.Handled = true;
    }

    private void OnPatchDoAfter(Entity<NicotinePatchItemComponent> entity, ref NicotinePatchDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target == null)
            return;

        var target = args.Args.Target.Value;
        var user = args.Args.User;

        var patch = EnsureComp<NicotinePatchComponent>(target);
        patch.RemainingDuration = TimeSpan.FromMinutes(5);
        patch.NextTransfer = _timing.CurTime + patch.TransferInterval;

        _popup.PopupEntity(Loc.GetString("smoking-patch-applied"), target, user);
        QueueDel(entity.Owner);
        args.Handled = true;
    }

    /// <summary>
    /// Регистрация активной затяжки для персонажа.
    /// </summary>
    public void RegisterPuff(EntityUid uid)
    {
        var comp = EnsureComp<NicotineAddictionComponent>(uid);
        comp.PuffCount++;

        // Зависимость наступает после 6 затяжек (~1-2 выкуренных сигареты)
        if (comp.PuffCount >= 6 && !comp.IsAddicted)
        {
            comp.IsAddicted = true;
            _popup.PopupEntity(Loc.GetString("smoking-addiction-formed"), uid, uid);
        }

        var wasInWithdrawal = comp.InWithdrawal;
        comp.InWithdrawal = false;
        comp.NextWithdrawalTime = _timing.CurTime + TimeSpan.FromMinutes(5);
        comp.NextWithdrawalSymptomTime = comp.NextWithdrawalTime;
        Dirty(uid, comp);

        // Накладываем позитивный эффект никотинового расслабления
        RaiseLocalEvent(uid, new MoodEffectEvent("NicotineSatisfied"));

        // Если была ломка — снимаем негативный мудлет и сообщаем об облегчении
        if (wasInWithdrawal)
        {
            RaiseLocalEvent(uid, new MoodRemoveEffectEvent("NicotineWithdrawal"));
            _popup.PopupEntity(Loc.GetString("smoking-withdrawal-relief"), uid, uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        var curTime = _timing.CurTime;

        // 1. Обработка никотиновых пластырей
        var patchQuery = EntityQueryEnumerator<NicotinePatchComponent, BloodstreamComponent>();
        while (patchQuery.MoveNext(out var uid, out var patch, out var bloodstream))
        {
            patch.RemainingDuration -= UpdateInterval;
            if (curTime >= patch.NextTransfer)
            {
                patch.NextTransfer = curTime + patch.TransferInterval;
                var dose = new Solution("Nicotine", FixedPoint2.New(patch.NicotinePerTransfer));
                _reactive.DoEntityReaction(uid, dose, ReactionMethod.Ingestion);
                _bloodstream.TryAddToBloodstream((uid, bloodstream), dose);
            }

            if (patch.RemainingDuration <= TimeSpan.Zero)
            {
                RemComp<NicotinePatchComponent>(uid);
                _popup.PopupEntity(Loc.GetString("smoking-patch-expired"), uid, uid);
            }
        }

        // 2. Обработка никотиновой зависимости и ломки
        var query = EntityQueryEnumerator<NicotineAddictionComponent, BloodstreamComponent>();

        while (query.MoveNext(out var uid, out var comp, out var bloodstream))
        {
            // Проверяем лечение специализированными медикаментами (Haloperidol, Pax, AmbuzolPlus)
            if (_solution.TryGetSolution(uid, bloodstream.BloodSolutionName, out _, out var chemSoln))
            {
                if (chemSoln.GetTotalPrototypeQuantity("Haloperidol") >= 0.5f ||
                    chemSoln.GetTotalPrototypeQuantity("Pax") >= 0.5f ||
                    chemSoln.GetTotalPrototypeQuantity("AmbuzolPlus") >= 0.5f)
                {
                    RaiseLocalEvent(uid, new MoodRemoveEffectEvent("NicotineWithdrawal"));
                    RemComp<NicotineAddictionComponent>(uid);
                    _popup.PopupEntity(Loc.GetString("smoking-addiction-cured"), uid, uid);
                    continue;
                }

                // Наличие никотина в крови (из пластыря, жвачки, пара или пассивного курения) утоляет ломку
                if (chemSoln.GetTotalPrototypeQuantity("Nicotine") >= 0.1f)
                {
                    if (comp.InWithdrawal)
                    {
                        comp.InWithdrawal = false;
                        RaiseLocalEvent(uid, new MoodRemoveEffectEvent("NicotineWithdrawal"));
                        _popup.PopupEntity(Loc.GetString("smoking-withdrawal-relief"), uid, uid);
                    }

                    comp.NextWithdrawalTime = curTime + TimeSpan.FromMinutes(5);
                    comp.NextWithdrawalSymptomTime = comp.NextWithdrawalTime;
                    Dirty(uid, comp);
                }
            }

            if (!comp.IsAddicted)
                continue;

            // Наступление никотиновой ломки
            if (curTime >= comp.NextWithdrawalTime)
            {
                if (!comp.InWithdrawal)
                {
                    comp.InWithdrawal = true;
                    RaiseLocalEvent(uid, new MoodEffectEvent("NicotineWithdrawal"));
                    comp.NextWithdrawalSymptomTime = curTime;
                    Dirty(uid, comp);
                }

                // Периодические симптомы ломки (вздохи, дрожь рук, одышка/урон стамине, навязчивые мысли)
                if (curTime >= comp.NextWithdrawalSymptomTime)
                {
                    _jittering.DoJitter(uid, TimeSpan.FromSeconds(3), true, amplitude: 6f, frequency: 4f);
                    _chat.TryEmoteWithChat(uid, "Sigh");
                    _stamina.TakeStaminaDamage(uid, 15f);

                    var symptoms = new[] { "smoking-craving-1", "smoking-craving-2", "smoking-craving-3" };
                    var msg = _random.Pick(symptoms);
                    _popup.PopupEntity(Loc.GetString(msg), uid, uid);

                    comp.NextWithdrawalSymptomTime = curTime + TimeSpan.FromSeconds(_random.Next(35, 55));
                    Dirty(uid, comp);
                }
            }
        }
    }
}

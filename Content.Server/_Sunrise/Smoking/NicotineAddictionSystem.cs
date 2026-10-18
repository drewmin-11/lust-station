using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared._Sunrise.Mood;
using Content.Shared._Sunrise.Smoking;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Jittering;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Sunrise.Smoking;

/// <summary>
/// Система никотиновой зависимости, ломки и лечения медикаментами.
/// </summary>
public sealed partial class NicotineAddictionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedJitteringSystem _jittering = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    private TimeSpan _nextUpdate = TimeSpan.Zero;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(3);

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
        var query = EntityQueryEnumerator<NicotineAddictionComponent, BloodstreamComponent>();

        while (query.MoveNext(out var uid, out var comp, out var bloodstream))
        {
            // 1. Проверяем лечение специализированными медикаментами (Haloperidol, Pax, AmbuzolPlus)
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
            }

            if (!comp.IsAddicted)
                continue;

            // 2. Наступление никотиновой ломки
            if (curTime >= comp.NextWithdrawalTime)
            {
                if (!comp.InWithdrawal)
                {
                    comp.InWithdrawal = true;
                    RaiseLocalEvent(uid, new MoodEffectEvent("NicotineWithdrawal"));
                    comp.NextWithdrawalSymptomTime = curTime;
                    Dirty(uid, comp);
                }

                // 3. Периодические симптомы ломки (вздохи, дрожь рук, навязчивые мысли)
                if (curTime >= comp.NextWithdrawalSymptomTime)
                {
                    _jittering.DoJitter(uid, TimeSpan.FromSeconds(3), true, amplitude: 6f, frequency: 4f);
                    _chat.TryEmoteWithChat(uid, "Sigh");

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

using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Sunrise.Smoking;

/// <summary>
/// Компонент никотиновой зависимости персонажа.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NicotineAddictionComponent : Component
{
    /// <summary>
    /// Количество сделанных затяжек.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int PuffCount;

    /// <summary>
    /// Зависим ли персонаж от никотина.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsAddicted;

    /// <summary>
    /// Время, до которого действует никотиновое насыщение.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan NextWithdrawalTime = TimeSpan.Zero;

    /// <summary>
    /// Находится ли персонаж в состоянии никотиновой ломки.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool InWithdrawal;

    /// <summary>
    /// Время следующего приступа ломки (вздох, дрожь рук, поп-ап).
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan NextWithdrawalSymptomTime = TimeSpan.Zero;
}

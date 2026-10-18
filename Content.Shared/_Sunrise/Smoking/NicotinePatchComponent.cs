using Robust.Shared.GameStates;

namespace Content.Shared._Sunrise.Smoking;

/// <summary>
/// Компонент на персонаже, получающем никотин от наклеенного пластыря.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NicotinePatchComponent : Component
{
    [DataField("remainingDuration")]
    public TimeSpan RemainingDuration = TimeSpan.FromMinutes(5);

    [DataField("transferInterval")]
    public TimeSpan TransferInterval = TimeSpan.FromSeconds(10);

    public TimeSpan NextTransfer = TimeSpan.Zero;

    [DataField("nicotinePerTransfer")]
    public float NicotinePerTransfer = 0.05f;
}

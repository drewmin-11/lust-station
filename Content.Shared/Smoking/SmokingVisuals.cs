using Robust.Shared.Serialization;

namespace Content.Shared.Smoking
{
    [Serializable, NetSerializable]
    public enum SmokingVisuals : byte
    {
        Smoking,
        // Sunrise-Start: визуальное состояние вставленного в вейп картриджа
        CartridgeState,
        // Sunrise-End
    }
}

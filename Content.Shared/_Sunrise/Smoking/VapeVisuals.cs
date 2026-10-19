using Robust.Shared.Serialization;

namespace Content.Shared._Sunrise.Smoking
{
    [Serializable, NetSerializable]
    public enum VapeVisuals : byte
    {
        HasCartridge,
    }

    [Serializable, NetSerializable]
    public enum VapeVisualLayers : byte
    {
        Base,
    }
}

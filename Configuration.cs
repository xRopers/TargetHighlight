using System;
using System.Numerics;
using Dalamud.Configuration;

namespace TargetHighlight;

public enum FlashMode
{
    /// <summary>Leave the game's flash alone.</summary>
    GameDefault,
    /// <summary>No flash at all.</summary>
    Disabled,
    /// <summary>Replace the game's flash with <see cref="Configuration.Color"/>, intensity, duration and curve.</summary>
    Custom,
}

public enum FadeCurve
{
    /// <summary>Matches the game's own fade, sampled from CharacterBase.Tint.</summary>
    GameLike,
    Linear,
    EaseOut,
    Smooth,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const float DefaultIntensity = 10f;
    public const float DefaultDuration = 0.7f;

    public int Version { get; set; } = 1;

    /// <summary>Version 0 setting; only read to migrate to <see cref="Mode"/>.</summary>
    public bool Enabled { get; set; } = true;

    public FlashMode Mode { get; set; } = FlashMode.Disabled;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = DefaultIntensity;
    public float Duration { get; set; } = DefaultDuration;
    public FadeCurve Curve { get; set; } = FadeCurve.GameLike;

    /// <summary>Keep the custom tint at full strength while the object stays targeted; the fade starts on untarget.</summary>
    public bool LockWhileTargeted { get; set; }

    public void Migrate()
    {
        if (Version >= 1)
            return;

        Mode = Enabled ? FlashMode.Disabled : FlashMode.GameDefault;
        Version = 1;
        Save();
    }

    public void ResetCustomToGameLook()
    {
        Color = Vector3.One;
        Intensity = DefaultIntensity;
        Duration = DefaultDuration;
        Curve = FadeCurve.GameLike;
        LockWhileTargeted = false;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

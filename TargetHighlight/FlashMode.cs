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

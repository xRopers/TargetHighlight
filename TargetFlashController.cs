using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace TargetHighlight;

/// <summary>
/// When something is targeted, the game sets the model's CharacterBase.Tint RGB to 10 and eases it back to 1 over
/// ~40 frames (the shader squares Tint into MulColor). Every Tint write goes through CharacterBase.SetTint,
/// so this hooks that to either block the flash or replace it with a custom colour and fade.
/// Addresses and offsets come from ClientStructs (see <see cref="SetTintResolver"/>) so game patches don't break it.
/// </summary>
public sealed unsafe class TargetFlashController : IDisposable
{
    // The flash lasts ~40 frames; keep covering a model for a while after it stops being the target
    // so switching targets quickly doesn't let the old target's flash finish.
    private static readonly TimeSpan RecentTargetWindow = TimeSpan.FromSeconds(3);

    // Normalised game fade (Tint - 1) / 9, sampled every 4 frames from flash_record.txt.
    private static readonly float[] GameFade = [1f, 0.668f, 0.485f, 0.377f, 0.300f, 0.237f, 0.184f, 0.138f, 0.096f, 0.056f, 0.019f, 0f];

    private delegate void SetTintDelegate(nint characterBase, Vector4* tint);

    private sealed class CustomFlash
    {
        public required nint GameObject;
        public DateTime Start;
        public float Alpha;
        public float LastGameValue;
        public bool Finished;
    }

    private readonly Configuration configuration;
    private readonly Hook<SetTintDelegate>? hook;
    private readonly Dictionary<nint, (nint GameObject, DateTime Seen)> recentTargets = [];
    private readonly Dictionary<nint, CustomFlash> customFlashes = [];
    private bool previewRequested;

    public bool HookActive => hook is { IsEnabled: true };

    /// <summary>Where SetTint was found, shown in the Diagnostics tab.</summary>
    public string HookSource { get; } = "not found";

    public TargetFlashController(Configuration configuration)
    {
        this.configuration = configuration;

        try
        {
            var (address, source) = SetTintResolver.Resolve();
            HookSource = source;
            if (address != 0)
            {
                hook = Plugin.GameInterop.HookFromAddress<SetTintDelegate>(address, SetTintDetour);
                hook.Enable();
                Plugin.Log.Information($"Hooked CharacterBase.SetTint at 0x{address:X} ({source})");
            }
            else
            {
                Plugin.Log.Error($"Could not find CharacterBase.SetTint ({source}); the game may have been patched.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not hook CharacterBase.SetTint; the game may have been patched.");
        }

        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        foreach (var (drawObject, flash) in customFlashes)
        {
            if (!flash.Finished && IsAlive(drawObject, flash.GameObject))
                WriteTint(drawObject, new Vector4(1f, 1f, 1f, flash.Alpha));
        }

        hook?.Dispose();
    }

    /// <summary>Plays the custom flash on the current target on the next frame.</summary>
    public void RequestPreview() => previewRequested = true;

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.Now;
        Remember(Plugin.TargetManager.Target?.Address ?? 0, now);
        Remember(Plugin.TargetManager.SoftTarget?.Address ?? 0, now);

        foreach (var stale in recentTargets.Where(kv => now - kv.Value.Seen > RecentTargetWindow).Select(kv => kv.Key).ToList())
            recentTargets.Remove(stale);

        if (previewRequested)
        {
            previewRequested = false;
            var target = Plugin.TargetManager.Target?.Address ?? 0;
            var drawObject = DrawObjectOf(target);
            if (drawObject != 0 && configuration.Mode == FlashMode.Custom)
                StartCustomFlash(drawObject, target, ReadTint(drawObject).W, gameValue: 1f);
        }

        if (configuration.Mode == FlashMode.Custom && configuration.LockWhileTargeted)
        {
            LockTint(Plugin.TargetManager.Target?.Address ?? 0, now);
            LockTint(Plugin.TargetManager.SoftTarget?.Address ?? 0, now);
        }

        UpdateCustomFlashes(now);
    }

    /// <summary>Holds the flash at full strength by restarting it every frame while the object stays targeted.</summary>
    private void LockTint(nint gameObject, DateTime now)
    {
        var drawObject = DrawObjectOf(gameObject);
        if (drawObject == 0)
            return;

        if (customFlashes.TryGetValue(drawObject, out var flash) && !flash.Finished)
            flash.Start = now;
        else
            StartCustomFlash(drawObject, gameObject, ReadTint(drawObject).W, flash?.LastGameValue ?? 1f);
    }

    private void UpdateCustomFlashes(DateTime now)
    {
        foreach (var (drawObject, flash) in customFlashes.ToList())
        {
            if (!IsAlive(drawObject, flash.GameObject) || now - flash.Start > RecentTargetWindow + TimeSpan.FromSeconds(Math.Max(configuration.Duration, 0f)))
            {
                customFlashes.Remove(drawObject);
                continue;
            }

            if (flash.Finished)
                continue;

            if (configuration.Mode != FlashMode.Custom || Progress(flash, now) >= 1f)
            {
                WriteTint(drawObject, new Vector4(1f, 1f, 1f, flash.Alpha));
                flash.Finished = true;
                continue;
            }

            WriteTint(drawObject, CustomTint(flash, now));
        }
    }

    private void SetTintDetour(nint characterBase, Vector4* tint)
    {
        var mode = configuration.Mode;
        if (mode == FlashMode.GameDefault || tint == null)
        {
            hook!.Original(characterBase, tint);
            return;
        }

        var brightened = tint->X > 1f || tint->Y > 1f || tint->Z > 1f;

        if (mode == FlashMode.Custom && customFlashes.TryGetValue(characterBase, out var flash))
        {
            // The game's own flash restarted (value jumped back up): restart ours with it.
            if (brightened && tint->X > flash.LastGameValue + 0.5f)
                StartCustomFlash(characterBase, flash.GameObject, tint->W, tint->X);
            flash.LastGameValue = brightened ? tint->X : 1f;
            flash.Alpha = tint->W;

            var replacement = flash.Finished ? Unbrightened(*tint) : CustomTint(flash, DateTime.Now);
            hook!.Original(characterBase, &replacement);
            return;
        }

        // The flash starts in the same frame the target changes, before OnUpdate has seen the new target,
        // so also check the live target here (only reached for brightened tints, which are rare).
        if (brightened && FindTargetGameObject(characterBase) is var gameObject and not 0)
        {
            if (mode == FlashMode.Custom)
            {
                var started = StartCustomFlash(characterBase, gameObject, tint->W, tint->X);
                var replacement = CustomTint(started, DateTime.Now);
                hook!.Original(characterBase, &replacement);
            }
            else
            {
                var replacement = Unbrightened(*tint);
                hook!.Original(characterBase, &replacement);
            }

            return;
        }

        hook!.Original(characterBase, tint);
    }

    private CustomFlash StartCustomFlash(nint drawObject, nint gameObject, float alpha, float gameValue)
    {
        if (!customFlashes.TryGetValue(drawObject, out var flash))
        {
            flash = new CustomFlash { GameObject = gameObject };
            customFlashes[drawObject] = flash;
        }

        flash.GameObject = gameObject;
        flash.Start = DateTime.Now;
        flash.Alpha = alpha;
        flash.LastGameValue = gameValue;
        flash.Finished = false;
        return flash;
    }

    private float Progress(CustomFlash flash, DateTime now)
    {
        var duration = configuration.Duration;
        return duration <= 0f ? 1f : (float)((now - flash.Start).TotalSeconds / duration);
    }

    private Vector4 CustomTint(CustomFlash flash, DateTime now)
    {
        var strength = Fade(configuration.Curve, Math.Clamp(Progress(flash, now), 0f, 1f));
        var boost = configuration.Color * ((configuration.Intensity - 1f) * strength);
        return new Vector4(Vector3.Max(Vector3.One + boost, Vector3.Zero), flash.Alpha);
    }

    /// <summary>Remaining flash strength, 1 at the start of the fade and 0 at the end.</summary>
    public static float Fade(FadeCurve curve, float t) => curve switch
    {
        FadeCurve.Linear => 1f - t,
        FadeCurve.EaseOut => (1f - t) * (1f - t),
        FadeCurve.Smooth => 1f - t * t * (3f - 2f * t),
        _ => SampleGameFade(t),
    };

    private static float SampleGameFade(float t)
    {
        var position = t * (GameFade.Length - 1);
        var index = Math.Min((int)position, GameFade.Length - 2);
        return GameFade[index] + (GameFade[index + 1] - GameFade[index]) * (position - index);
    }

    private static Vector4 Unbrightened(Vector4 tint) =>
        new(MathF.Min(tint.X, 1f), MathF.Min(tint.Y, 1f), MathF.Min(tint.Z, 1f), tint.W);

    private void Remember(nint gameObject, DateTime now)
    {
        var drawObject = DrawObjectOf(gameObject);
        if (drawObject != 0)
            recentTargets[drawObject] = (gameObject, now);
    }

    private nint FindTargetGameObject(nint drawObject)
    {
        if (recentTargets.TryGetValue(drawObject, out var recent))
            return recent.GameObject;

        foreach (var target in new[] { Plugin.TargetManager.Target, Plugin.TargetManager.SoftTarget })
        {
            var address = target?.Address ?? 0;
            if (address != 0 && DrawObjectOf(address) == drawObject)
                return address;
        }

        return 0;
    }

    /// <summary>Whether the game object still exists and still owns this model, so it is safe to write to.</summary>
    private static bool IsAlive(nint drawObject, nint gameObject)
    {
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.Address == gameObject)
                return DrawObjectOf(gameObject) == drawObject;
        }

        return false;
    }

    private static nint DrawObjectOf(nint gameObject) =>
        gameObject == 0 ? 0 : (nint)((GameObject*)gameObject)->DrawObject;

    private static Vector4 ReadTint(nint drawObject) => ((CharacterBase*)drawObject)->Tint;

    private void WriteTint(nint drawObject, Vector4 tint) => hook?.Original(drawObject, &tint);
}

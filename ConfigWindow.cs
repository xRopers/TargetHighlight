using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace TargetHighlight;

public sealed class ConfigWindow : Window
{
    private const float WindowWidth = 440f;
    private const float PickerSize = 190f;
    private const float LabelColumnWidth = 110f;
    private const float GraphHeight = 56f;
    private const int GraphSegments = 64;

    private static readonly Vector4 Accent = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.65f, 1f);
    private static readonly Vector4 Warning = new(1f, 0.45f, 0.45f, 1f);

    private static readonly (FlashMode Mode, string Label, string Tooltip)[] Modes =
    [
        (FlashMode.GameDefault, "Normal", "Leave the game's target flash as it is."),
        (FlashMode.Disabled, "No flash", "Targeting no longer brightens the model."),
        (FlashMode.Custom, "Custom", "Replace the flash with your own colour, brightness and fade."),
    ];

    private static readonly (FadeCurve Curve, string Label)[] Curves =
    [
        (FadeCurve.GameLike, "Game-like"),
        (FadeCurve.Linear, "Linear"),
        (FadeCurve.EaseOut, "Ease out (fast then slow)"),
        (FadeCurve.Smooth, "Smooth (slow start and end)"),
    ];

    private static readonly (string Name, Vector3 Color)[] Presets =
    [
        ("White", new(1f, 1f, 1f)),
        ("Red", new(1f, 0.15f, 0.15f)),
        ("Orange", new(1f, 0.55f, 0.1f)),
        ("Gold", new(1f, 0.85f, 0.2f)),
        ("Green", new(0.25f, 1f, 0.35f)),
        ("Cyan", new(0.2f, 0.9f, 1f)),
        ("Blue", new(0.25f, 0.45f, 1f)),
        ("Purple", new(0.7f, 0.3f, 1f)),
        ("Pink", new(1f, 0.35f, 0.8f)),
    ];

    private readonly Plugin plugin;
    private Vector3 hsv;
    private Vector3 hsvSource = new(float.NaN);
    private string hexBuffer = string.Empty;
    private Vector3 hexSource = new(float.NaN);
    private bool hexEditing;

    public ConfigWindow(Plugin plugin)
        : base("TargetHighlight###TargetHighlightConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(WindowWidth, 0f),
            MaximumSize = new Vector2(WindowWidth, 2000f),
        };
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##tabs"))
            return;

        if (ImGui.BeginTabItem("Flash"))
        {
            DrawFlashTab(plugin.Configuration);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Diagnostics"))
        {
            DrawDiagnosticsTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawFlashTab(Configuration config)
    {
        ImGui.Spacing();
        ImGui.TextColored(Muted, "When you target something");
        DrawModeSelector(config);

        if (!plugin.HookActive)
        {
            ImGui.Spacing();
            ImGui.TextColored(Warning, "Hook not active - the game may have updated. Check /xllog.");
        }

        if (config.Mode != FlashMode.Custom)
            return;

        var changed = false;

        SectionHeader("Colour");
        changed |= DrawColorSection(config);

        SectionHeader("Brightness & fade");
        changed |= DrawFadeSection(config);

        ImGui.Spacing();
        DrawFadeGraph(config);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        changed |= DrawFooterButtons(config);

        if (changed)
            config.Save();
    }

    private static void DrawModeSelector(Configuration config)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = (ImGui.GetContentRegionAvail().X - spacing * (Modes.Length - 1)) / Modes.Length;

        for (var i = 0; i < Modes.Length; i++)
        {
            var (mode, label, tooltip) = Modes[i];
            if (i > 0)
                ImGui.SameLine();

            var active = config.Mode == mode;
            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.45f, 0.75f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.50f, 0.82f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.22f, 0.40f, 0.68f, 1f));
            }

            if (ImGui.Button($"{label}##mode", new Vector2(width, 0f)) && !active)
            {
                config.Mode = mode;
                config.Save();
            }

            if (active)
                ImGui.PopStyleColor(3);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
        }
    }

    private bool DrawColorSection(Configuration config)
    {
        var changed = false;
        var scale = ImGuiHelpers.GlobalScale;

        if (ImGui.BeginTable("##colorLayout", 2, ImGuiTableFlags.None))
        {
            ImGui.TableSetupColumn("picker", ImGuiTableColumnFlags.WidthFixed, PickerSize * scale);
            ImGui.TableSetupColumn("fields", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var color = config.Color;
            ImGui.SetNextItemWidth(PickerSize * scale);
            if (ImGui.ColorPicker3("##flashColor", ref color,
                    ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoSidePreview
                    | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel))
            {
                config.Color = color;
                changed = true;
            }

            ImGui.TableNextColumn();
            var fieldWidth = ImGui.GetContentRegionAvail().X;
            ImGui.ColorButton("##swatch", new Vector4(config.Color, 1f),
                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(fieldWidth, 40f * scale));

            ImGui.Spacing();
            changed |= DrawHexInput(config, fieldWidth);
            ImGui.Spacing();
            changed |= DrawColorInputs(config, fieldWidth);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        changed |= DrawPresets(config);
        return changed;
    }

    private bool DrawHexInput(Configuration config, float width)
    {
        // Don't overwrite the text while it is being typed.
        if (config.Color != hexSource && !hexEditing)
        {
            hexBuffer = $"#{ToByte(config.Color.X):X2}{ToByte(config.Color.Y):X2}{ToByte(config.Color.Z):X2}";
            hexSource = config.Color;
        }

        ImGui.SetNextItemWidth(width);
        var edited = ImGui.InputText("##hex", ref hexBuffer, 7);
        hexEditing = ImGui.IsItemActive();
        if (!edited)
            return false;

        var digits = hexBuffer.TrimStart('#');
        if (digits.Length != 6 || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        config.Color = new Vector3((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF) / 255f;
        hexSource = config.Color;
        return true;
    }

    /// <summary>Typed R/G/B (0-255) and H/S/V (0-360, 0-100, 0-100) fields kept in sync with the picker.</summary>
    private bool DrawColorInputs(Configuration config, float width)
    {
        // Keep HSV as our own state so hue survives grey colours (saturation or value 0), where RGB has no hue.
        if (config.Color != hsvSource)
        {
            var (h, s, v) = RgbToHsv(config.Color);
            hsv = new Vector3(s == 0f || v == 0f ? hsv.X : h, v == 0f ? hsv.Y : s, v);
            hsvSource = config.Color;
        }

        var rgb = new[] { ToByte(config.Color.X), ToByte(config.Color.Y), ToByte(config.Color.Z) };
        var hsvInts = new[] { (int)MathF.Round(hsv.X * 360f), (int)MathF.Round(hsv.Y * 100f), (int)MathF.Round(hsv.Z * 100f) };
        string[] rgbLabels = ["R", "G", "B"];
        string[] hsvLabels = ["H", "S", "V"];
        int[] rgbMax = [255, 255, 255];
        int[] hsvMax = [360, 100, 100];

        var labelWidth = ImGui.CalcTextSize("W").X + ImGui.GetStyle().ItemInnerSpacing.X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var inputWidth = (width - spacing - labelWidth * 2f) / 2f;

        var rgbChanged = false;
        var hsvChanged = false;
        for (var i = 0; i < 3; i++)
        {
            rgbChanged |= DrawLabeledInt(rgbLabels[i], ref rgb[i], rgbMax[i], labelWidth, inputWidth);
            ImGui.SameLine(0f, spacing);
            hsvChanged |= DrawLabeledInt(hsvLabels[i], ref hsvInts[i], hsvMax[i], labelWidth, inputWidth);
        }

        if (rgbChanged)
        {
            config.Color = new Vector3(rgb[0], rgb[1], rgb[2]) / 255f;
            return true;
        }

        if (hsvChanged)
        {
            hsv = new Vector3(hsvInts[0] / 360f, hsvInts[1] / 100f, hsvInts[2] / 100f);
            config.Color = HsvToRgb(hsv);
            hsvSource = config.Color;
            return true;
        }

        return false;
    }

    private static bool DrawLabeledInt(string label, ref int value, int max, float labelWidth, float inputWidth)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Muted, label);
        ImGui.SameLine(0f, labelWidth - ImGui.CalcTextSize(label).X);
        ImGui.SetNextItemWidth(inputWidth);

        var edited = value;
        if (!ImGui.InputInt($"##{label}", ref edited, 0, 0))
            return false;

        value = Math.Clamp(edited, 0, max);
        return true;
    }

    private static bool DrawPresets(Configuration config)
    {
        var changed = false;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TextColored(Muted, "Presets");
        ImGui.SameLine();

        foreach (var (name, color) in Presets)
        {
            if (ImGui.ColorButton($"{name}##preset", new Vector4(color, 1f), ImGuiColorEditFlags.NoDragDrop, new Vector2(22f * scale)))
            {
                config.Color = color;
                changed = true;
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();
        return changed;
    }

    private static bool DrawFadeSection(Configuration config)
    {
        var changed = false;
        if (!ImGui.BeginTable("##fadeLayout", 2, ImGuiTableFlags.None))
            return false;

        ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, LabelColumnWidth * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("control", ImGuiTableColumnFlags.WidthStretch);

        RowLabel("Intensity", "Peak brightness. 10x is the game's normal flash, 1x is no change, below 1x darkens.");
        var intensity = config.Intensity;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##intensity", ref intensity, 0f, 20f, "%.1fx"))
        {
            config.Intensity = Math.Clamp(intensity, 0f, 50f);
            changed = true;
        }

        RowLabel("Hold", "Keep the colour at full strength for as long as the object stays targeted.");
        var locked = config.LockWhileTargeted;
        if (ImGui.Checkbox("Lock while targeted (no fade)", ref locked))
        {
            config.LockWhileTargeted = locked;
            changed = true;
        }

        RowLabel("Fade-out", config.LockWhileTargeted
            ? "How long the colour takes to fade after you untarget."
            : "How long the flash takes to fade back to normal. The game's own flash is about 0.7 s.");
        var duration = config.Duration;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##duration", ref duration, 0.05f, 5f, config.LockWhileTargeted ? "%.2f s after untarget" : "%.2f s"))
        {
            config.Duration = Math.Clamp(duration, 0.01f, 30f);
            changed = true;
        }

        RowLabel("Curve", "Shape of the fade.");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##curve", Array.Find(Curves, c => c.Curve == config.Curve).Label ?? "Game-like"))
        {
            foreach (var (curve, label) in Curves)
            {
                if (ImGui.Selectable(label, config.Curve == curve))
                {
                    config.Curve = curve;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.EndTable();
        return changed;
    }

    private static void RowLabel(string label, string tooltip)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        ImGui.TableNextColumn();
    }

    /// <summary>Plots brightness over time in the flash colour, with the held section when the tint is locked.</summary>
    private static void DrawFadeGraph(Configuration config)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, GraphHeight * scale);
        var bottomRight = origin + size;

        drawList.AddRectFilled(origin, bottomRight, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), 4f * scale);
        drawList.AddRect(origin, bottomRight, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), 4f * scale);

        var lineColor = new Vector4(Vector3.Max(config.Color, new Vector3(0.25f)), 1f);
        var fill = ImGui.GetColorU32(lineColor with { W = 0.22f });
        var line = ImGui.GetColorU32(lineColor);

        var pad = 6f * scale;
        var left = origin.X + pad;
        var right = bottomRight.X - pad;
        var top = origin.Y + pad;
        var bottom = bottomRight.Y - pad;
        var holdWidth = config.LockWhileTargeted ? (right - left) * 0.3f : 0f;
        var peak = MathF.Max(config.Intensity, 1f);
        var baseline = config.Intensity >= 1f ? 1f : config.Intensity;

        float YFor(float strength)
        {
            var value = 1f + (config.Intensity - 1f) * strength;
            var normalized = peak - baseline <= 0f ? 0f : (value - baseline) / (peak - baseline);
            return bottom - (bottom - top) * Math.Clamp(normalized, 0f, 1f);
        }

        if (holdWidth > 0f)
        {
            var y = YFor(1f);
            drawList.AddRectFilled(new Vector2(left, y), new Vector2(left + holdWidth, bottom), fill);
            drawList.AddLine(new Vector2(left, y), new Vector2(left + holdWidth, y), line, 2f * scale);
            drawList.AddLine(new Vector2(left + holdWidth, top), new Vector2(left + holdWidth, bottom),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.15f)), 1f);
        }

        var fadeLeft = left + holdWidth;
        var previous = new Vector2(fadeLeft, YFor(TargetFlashController.Fade(config.Curve, 0f)));
        for (var i = 1; i <= GraphSegments; i++)
        {
            var t = i / (float)GraphSegments;
            var point = new Vector2(fadeLeft + (right - fadeLeft) * t, YFor(TargetFlashController.Fade(config.Curve, t)));
            drawList.AddQuadFilled(previous, point, new Vector2(point.X, bottom), new Vector2(previous.X, bottom), fill);
            drawList.AddLine(previous, point, line, 2f * scale);
            previous = point;
        }

        var caption = config.LockWhileTargeted
            ? $"held while targeted, then {config.Duration:0.00} s fade"
            : $"{config.Duration:0.00} s fade";
        var captionSize = ImGui.CalcTextSize(caption);
        drawList.AddText(new Vector2(right - captionSize.X, top), ImGui.GetColorU32(Muted), caption);

        ImGui.Dummy(size);
    }

    private bool DrawFooterButtons(Configuration config)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = (ImGui.GetContentRegionAvail().X - spacing) / 2f;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.45f, 0.75f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.50f, 0.82f, 1f));
        if (ImGui.Button("Preview on target", new Vector2(width, 0f)))
            plugin.Controller.RequestPreview();
        ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Plays the custom flash on your current target.");

        ImGui.SameLine();
        if (!ImGui.Button("Reset to game look", new Vector2(width, 0f)))
            return false;

        config.ResetCustomToGameLook();
        return true;
    }

    private void DrawDiagnosticsTab()
    {
        ImGui.Spacing();
        ImGui.TextWrapped("Records which values on the target's model change for a couple of seconds after you target it. " +
                          "Useful for checking the flash after a game patch.");
        ImGui.Spacing();

        var armed = plugin.Recorder.Armed;
        if (ImGui.Checkbox("Record on target change", ref armed))
            plugin.Recorder.Armed = armed;

        ImGui.TextColored(Muted, "Status");
        ImGui.SameLine();
        ImGui.TextUnformatted(plugin.Recorder.Status);

        ImGui.TextColored(Muted, "Output");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy path"))
            ImGui.SetClipboardText(plugin.Recorder.OutputPath);
        ImGui.TextWrapped(plugin.Recorder.OutputPath);

        ImGui.Spacing();
        ImGui.TextColored(Muted, "Hook");
        ImGui.SameLine();
        if (plugin.HookActive)
            ImGui.TextColored(new Vector4(0.45f, 0.9f, 0.5f, 1f), "active");
        else
            ImGui.TextColored(Warning, "not active");
    }

    private static void SectionHeader(string title)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(Accent, title);
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static int ToByte(float channel) => (int)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);

    private static (float H, float S, float V) RgbToHsv(Vector3 rgb)
    {
        var max = MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z));
        var min = MathF.Min(rgb.X, MathF.Min(rgb.Y, rgb.Z));
        var delta = max - min;

        var h = 0f;
        if (delta > 0f)
        {
            if (max == rgb.X)
                h = (rgb.Y - rgb.Z) / delta % 6f;
            else if (max == rgb.Y)
                h = (rgb.Z - rgb.X) / delta + 2f;
            else
                h = (rgb.X - rgb.Y) / delta + 4f;
            h /= 6f;
            if (h < 0f)
                h += 1f;
        }

        return (h, max == 0f ? 0f : delta / max, max);
    }

    private static Vector3 HsvToRgb(Vector3 hsv)
    {
        var h = hsv.X % 1f * 6f;
        var c = hsv.Z * hsv.Y;
        var x = c * (1f - MathF.Abs(h % 2f - 1f));
        var m = hsv.Z - c;
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return new Vector3(r + m, g + m, b + m);
    }
}

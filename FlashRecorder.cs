using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace TargetHighlight;

/// <summary>
/// Diagnostic: when the target changes, snapshots the target's GameObject and DrawObject memory every frame,
/// then reports the fields that briefly move away from their settled value (what a flash looks like).
/// </summary>
public sealed unsafe class FlashRecorder : IDisposable
{
    private const int GameObjectSize = 0x2400;
    private const int DrawObjectSize = 0xA00;
    private const int FrameCount = 120;

    private readonly string outputPath;
    private readonly List<byte[]> gameObjectFrames = [];
    private readonly List<byte[]> drawObjectFrames = [];
    private nint lastTarget;
    private nint recordingTarget;
    private nint recordingDrawObject;
    private int recordingNumber;

    public bool Armed { get; set; }
    public string Status { get; private set; } = "Idle";

    public FlashRecorder(string outputDirectory)
    {
        outputPath = Path.Combine(outputDirectory, "flash_record.txt");
        Plugin.Framework.Update += OnUpdate;
    }

    public string OutputPath => outputPath;

    public void Dispose() => Plugin.Framework.Update -= OnUpdate;

    private void OnUpdate(IFramework framework)
    {
        var target = Plugin.TargetManager.Target?.Address ?? 0;

        if (recordingTarget != 0)
        {
            if (target != recordingTarget)
            {
                Status = "Target changed mid-recording; discarded.";
                ResetRecording();
            }
            else
            {
                Capture();
                if (gameObjectFrames.Count >= FrameCount)
                {
                    WriteReport();
                    ResetRecording();
                }
            }
        }
        else if (Armed && target != 0 && target != lastTarget)
        {
            recordingTarget = target;
            recordingDrawObject = DrawObjectOf(target);
            Status = $"Recording {Plugin.TargetManager.Target?.Name}...";
            Capture();
        }

        lastTarget = target;
    }

    private void Capture()
    {
        gameObjectFrames.Add(Read(recordingTarget, GameObjectSize));
        var drawObject = DrawObjectOf(recordingTarget);
        drawObjectFrames.Add(drawObject == recordingDrawObject && drawObject != 0
            ? Read(drawObject, DrawObjectSize)
            : new byte[DrawObjectSize]);
    }

    private static nint DrawObjectOf(nint gameObject) => (nint)((GameObject*)gameObject)->DrawObject;

    private static byte[] Read(nint address, int size)
    {
        var buffer = new byte[size];
        Marshal.Copy(address, buffer, 0, size);
        return buffer;
    }

    private void ResetRecording()
    {
        recordingTarget = 0;
        recordingDrawObject = 0;
        gameObjectFrames.Clear();
        drawObjectFrames.Clear();
    }

    private void WriteReport()
    {
        recordingNumber++;
        var sb = new StringBuilder();
        sb.AppendLine($"=== Recording {recordingNumber} at {DateTime.Now:HH:mm:ss}: {Plugin.TargetManager.Target?.Name} " +
                      $"(GameObject 0x{recordingTarget:X}, DrawObject 0x{recordingDrawObject:X}), {gameObjectFrames.Count} frames");
        Report(sb, "GameObject", gameObjectFrames);
        Report(sb, "DrawObject", drawObjectFrames);
        sb.AppendLine();

        File.AppendAllText(outputPath, sb.ToString());
        Status = $"Recording {recordingNumber} saved.";
        Plugin.Log.Information($"Saved flash recording {recordingNumber} to {outputPath}");
    }

    private static void Report(StringBuilder sb, string label, List<byte[]> frames)
    {
        var n = frames.Count;
        var size = frames[0].Length;
        sb.AppendLine($"-- {label}: transient fields (offset, frames away from final, final, peak, samples every 4 frames)");

        for (var offset = 0; offset + 4 <= size; offset += 4)
        {
            var settled = BitConverter.ToUInt32(frames[n - 1], offset);
            var away = 0;
            var lastAway = -1;
            for (var i = 0; i < n; i++)
            {
                if (BitConverter.ToUInt32(frames[i], offset) != settled)
                {
                    away++;
                    lastAway = i;
                }
            }

            // A flash moves away from the settled value and comes back well before the recording ends.
            if (away == 0 || lastAway > n * 3 / 4)
                continue;

            var values = frames.Select(f => BitConverter.ToUInt32(f, offset)).ToArray();
            var peak = values.OrderByDescending(v => Distance(v, settled)).First();
            var samples = string.Join(" ", values.Where((_, i) => i % 4 == 0 && i <= lastAway + 4).Select(Format));
            sb.AppendLine($"  +0x{offset:X4}  away={away,3}  final={Format(settled),-12} peak={Format(peak),-12} | {samples}");
        }
    }

    private static double Distance(uint a, uint b)
    {
        var fa = BitConverter.UInt32BitsToSingle(a);
        var fb = BitConverter.UInt32BitsToSingle(b);
        return IsPlainFloat(fa) && IsPlainFloat(fb) ? Math.Abs(fa - fb) : Math.Abs((double)a - b);
    }

    private static bool IsPlainFloat(float f) => float.IsFinite(f) && (f == 0 || Math.Abs(f) is > 1e-6f and < 1e7f);

    private static string Format(uint raw)
    {
        var f = BitConverter.UInt32BitsToSingle(raw);
        return raw != 0 && IsPlainFloat(f) && raw > 0x00FFFFFF ? f.ToString("0.###") : $"#{raw:X}";
    }
}

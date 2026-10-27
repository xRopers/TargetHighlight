using System;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace TargetHighlight.Game;

/// <summary>
/// Finds CharacterBase.SetTint (vf24 on 2026.10.06) without hardcoding its address, slot or the Tint offset.
/// ClientStructs, which Dalamud updates for each game patch, supplies the CharacterBase vtable and the Tint offset;
/// SetTint is the tiny function that copies a Vector4 into Tint:
///     movups xmm0, [rdx] ; movups [rcx+Tint], xmm0 ; ret
/// </summary>
public static unsafe class SetTintResolver
{
    private const int MaxVtableSlots = 128;

    public static int TintOffset => (int)Marshal.OffsetOf<CharacterBase>(nameof(CharacterBase.Tint));

    /// <summary>The code pattern of SetTint for the current Tint offset, e.g. "0F 10 02 0F 11 81 90 02 00 00 C3".</summary>
    public static string Signature
    {
        get
        {
            var offset = BitConverter.GetBytes(TintOffset);
            return $"0F 10 02 0F 11 81 {offset[0]:X2} {offset[1]:X2} {offset[2]:X2} {offset[3]:X2} C3";
        }
    }

    /// <summary>Returns SetTint's address and how it was found, or 0 if nothing worked.</summary>
    public static (nint Address, string Source) Resolve()
    {
        // Dalamud scans an unmodified copy of the game code, so this still matches when SetTint is already hooked
        // (Dalamud leaves a hook's jump in place after the hook is disposed, e.g. after a plugin reload).
        if (Plugin.SigScanner.TryScanText(Signature, out var address))
        {
            var slot = VtableSlotOf(address);
            return (address, slot >= 0 ? $"CharacterBase vtable slot {slot}" : $"signature {Signature} (not in vtable)");
        }

        // Last resort: compare the live bytes of each vtable entry. Only works while SetTint is unhooked.
        var expected = SetTintBytes(TintOffset);
        var vtable = Vtable;
        for (var slot = 0; vtable != null && slot < MaxVtableSlots; slot++)
        {
            var function = vtable[slot];
            if (function == 0)
                break;
            if (IsInText(function, expected.Length) && new ReadOnlySpan<byte>((void*)function, expected.Length).SequenceEqual(expected))
                return (function, $"CharacterBase vtable slot {slot} (byte match)");
        }

        return (0, $"not found (signature {Signature})");
    }

    private static nint* Vtable => (nint*)CharacterBase.StaticVirtualTablePointer;

    /// <summary>Slot of <paramref name="function"/> in the CharacterBase vtable, or -1. Compares pointers only.</summary>
    private static int VtableSlotOf(nint function)
    {
        var vtable = Vtable;
        if (vtable == null)
            return -1;

        for (var slot = 0; slot < MaxVtableSlots; slot++)
        {
            if (vtable[slot] == 0)
                break;
            if (vtable[slot] == function)
                return slot;
        }

        return -1;
    }

    private static bool IsInText(nint address, int length)
    {
        var start = Plugin.SigScanner.TextSectionBase;
        return address >= start && address + length <= start + Plugin.SigScanner.TextSectionSize;
    }

    private static byte[] SetTintBytes(int tintOffset)
    {
        var offset = BitConverter.GetBytes(tintOffset);
        return [0x0F, 0x10, 0x02, 0x0F, 0x11, 0x81, offset[0], offset[1], offset[2], offset[3], 0xC3];
    }
}

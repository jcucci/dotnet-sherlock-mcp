using System.Reflection;
using System.Reflection.Emit;

namespace Sherlock.MCP.Runtime.Il;

internal enum IlRefKind
{
    None,
    Call,
    CallVirt,
    NewObj,
    LdFtn,
    LdVirtFtn,
    LdFld,
    LdsFld,
    StFld,
    StsFld
}

internal readonly record struct IlTokenRef(IlRefKind Kind, int Token);

// Walks raw IL bytes and yields the metadata-token operands of call/field instructions.
// The operand-size table is derived from System.Reflection.Emit.OpCodes so every opcode width is
// transcription-free: a wrong width would silently desync the instruction pointer.
internal static class IlInstructionReader
{
    private const int SwitchOperand = -2;

    private static readonly Dictionary<int, int> OperandWidths = BuildOperandWidths();

    internal static IEnumerable<IlTokenRef> ReadTokenInstructions(byte[]? il)
    {
        if (il is null || il.Length == 0) yield break;

        var ip = 0;
        var len = il.Length;

        while (ip < len)
        {
            int opcode = il[ip++];
            if (opcode == 0xFE)
            {
                if (ip >= len) yield break;
                opcode = 0xFE00 | il[ip++];
            }

            if (!OperandWidths.TryGetValue(opcode, out var width))
                yield break;

            if (width == SwitchOperand)
            {
                if (ip + 4 > len) yield break;
                var count = ReadInt32(il, ip);
                var next = (long)ip + 4 + ((long)count * 4);
                if (count < 0 || next > len) yield break;
                ip = (int)next;
                continue;
            }

            var kind = Classify(opcode);
            if (kind != IlRefKind.None)
            {
                if (ip + 4 > len) yield break;
                yield return new IlTokenRef(kind, ReadInt32(il, ip));
            }

            if (ip + width > len) yield break;
            ip += width;
        }
    }

    private static IlRefKind Classify(int opcode) => opcode switch
    {
        0x28 => IlRefKind.Call,
        0x6F => IlRefKind.CallVirt,
        0x73 => IlRefKind.NewObj,
        0xFE06 => IlRefKind.LdFtn,
        0xFE07 => IlRefKind.LdVirtFtn,
        0x7B => IlRefKind.LdFld,
        0x7E => IlRefKind.LdsFld,
        0x7D => IlRefKind.StFld,
        0x80 => IlRefKind.StsFld,
        _ => IlRefKind.None
    };

    private static int ReadInt32(byte[] il, int offset) =>
        il[offset] | (il[offset + 1] << 8) | (il[offset + 2] << 16) | (il[offset + 3] << 24);

    private static Dictionary<int, int> BuildOperandWidths()
    {
        var map = new Dictionary<int, int>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode op) continue;
            map[(ushort)op.Value] = WidthOf(op.OperandType);
        }
        return map;
    }

    private static int WidthOf(OperandType operandType) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget => 1,
        OperandType.ShortInlineI => 1,
        OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.ShortInlineR => 4,
        OperandType.InlineBrTarget => 4,
        OperandType.InlineField => 4,
        OperandType.InlineI => 4,
        OperandType.InlineMethod => 4,
        OperandType.InlineSig => 4,
        OperandType.InlineString => 4,
        OperandType.InlineTok => 4,
        OperandType.InlineType => 4,
        OperandType.InlineI8 => 8,
        OperandType.InlineR => 8,
        OperandType.InlineSwitch => SwitchOperand,
        _ => 0
    };
}

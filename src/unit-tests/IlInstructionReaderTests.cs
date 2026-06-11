using Sherlock.MCP.Runtime.Il;

namespace Sherlock.MCP.Tests;

public class IlInstructionReaderTests
{
    private const int SampleToken = 0x06000001;
    private static readonly byte[] SampleTokenBytes = { 0x01, 0x00, 0x00, 0x06 };

    [Fact]
    public void EmptyOrNull_YieldsNothing()
    {
        Assert.Empty(IlInstructionReader.ReadTokenInstructions(null));
        Assert.Empty(IlInstructionReader.ReadTokenInstructions(Array.Empty<byte>()));
    }

    [Fact]
    public void EightByteOperand_DoesNotDesyncFollowingCallToken()
    {
        // nop; ldc.i8 <8 bytes>; call <token>; ret
        var il = new List<byte> { 0x00, 0x21 };
        il.AddRange(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        il.Add(0x28);
        il.AddRange(SampleTokenBytes);
        il.Add(0x2A);

        var refs = IlInstructionReader.ReadTokenInstructions(il.ToArray()).ToArray();

        var single = Assert.Single(refs);
        Assert.Equal(IlRefKind.Call, single.Kind);
        Assert.Equal(SampleToken, single.Token);
    }

    [Fact]
    public void Switch_VariableOperand_DoesNotDesyncFollowingCallToken()
    {
        // switch (count=2) target0 target1; call <token>; ret
        var il = new List<byte> { 0x45 };
        il.AddRange(new byte[] { 0x02, 0x00, 0x00, 0x00 });
        il.AddRange(new byte[] { 0xAA, 0xAA, 0xAA, 0xAA });
        il.AddRange(new byte[] { 0xBB, 0xBB, 0xBB, 0xBB });
        il.Add(0x28);
        il.AddRange(SampleTokenBytes);
        il.Add(0x2A);

        var refs = IlInstructionReader.ReadTokenInstructions(il.ToArray()).ToArray();

        var single = Assert.Single(refs);
        Assert.Equal(IlRefKind.Call, single.Kind);
        Assert.Equal(SampleToken, single.Token);
    }

    [Fact]
    public void TrailingPrefixByte_StopsGracefully()
    {
        // call <token>; lone 0xFE prefix with no second byte
        var il = new List<byte> { 0x28 };
        il.AddRange(SampleTokenBytes);
        il.Add(0xFE);

        var refs = IlInstructionReader.ReadTokenInstructions(il.ToArray()).ToArray();

        var single = Assert.Single(refs);
        Assert.Equal(IlRefKind.Call, single.Kind);
    }

    [Fact]
    public void ClassifiesCallFieldAndNewObjOpcodes()
    {
        // newobj <tok>; callvirt <tok>; ldsfld <tok>; stfld <tok>; ret
        var il = new List<byte>();
        il.Add(0x73); il.AddRange(SampleTokenBytes);
        il.Add(0x6F); il.AddRange(SampleTokenBytes);
        il.Add(0x7E); il.AddRange(SampleTokenBytes);
        il.Add(0x7D); il.AddRange(SampleTokenBytes);
        il.Add(0x2A);

        var kinds = IlInstructionReader.ReadTokenInstructions(il.ToArray()).Select(r => r.Kind).ToArray();

        Assert.Equal(
            new[] { IlRefKind.NewObj, IlRefKind.CallVirt, IlRefKind.LdsFld, IlRefKind.StFld },
            kinds);
    }

    [Fact]
    public void TwoByteLdftn_IsClassified()
    {
        // ldftn (0xFE 0x06) <token>; ret
        var il = new List<byte> { 0xFE, 0x06 };
        il.AddRange(SampleTokenBytes);
        il.Add(0x2A);

        var single = Assert.Single(IlInstructionReader.ReadTokenInstructions(il.ToArray()).ToArray());
        Assert.Equal(IlRefKind.LdFtn, single.Kind);
    }
}

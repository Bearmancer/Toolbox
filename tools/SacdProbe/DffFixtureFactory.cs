using System.Linq;
using System.Text;

namespace SacdProbe;

internal enum FixtureCase
{
    Baseline,
    Id3Valid,
    Id3CorruptSize,
    ComtNonAscii,
    BracketedName,
    Id3CorruptPlusBracketed,
}

internal static class DffFixtureFactory
{
    private const int SampleRate = 2822400;
    private const short Channels = 2;
    private const double Seconds = 0.5;
    private const int DsdBytes = 352800; // DSD64 stereo 0.5s: 2822400/8 * 2 * 0.5

    private static readonly string WorkDir = @"C:\Temp\saracon-probe";

    public static string Build(FixtureCase c)
    {
        Directory.CreateDirectory(WorkDir);
        var name = c switch
        {
            FixtureCase.BracketedName or FixtureCase.Id3CorruptPlusBracketed
                => "Disc 10 [SACD] (1)-test.dff",
            _ => $"{c}.dff",
        };
        var path = Path.Combine(WorkDir, name);

        var body = new MemoryStream();
        void WriteChunk(Stream target, ReadOnlySpan<byte> id, ReadOnlySpan<byte> data)
        {
            target.Write(id);
            Span<byte> size = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)data.Length);
            target.Write(size);
            target.Write(data);
            if (data.Length % 2 != 0) target.WriteByte(0);
        }

        Span<byte> ver = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ver, 0x00000105);
        WriteChunk(body, "FVER"u8, ver.ToArray());

        var prop = new MemoryStream();
        {
            Span<byte> fsRate = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(fsRate, SampleRate);
            WriteChunkTo(prop, "FS  "u8, fsRate.ToArray());

            Span<byte> chnl = stackalloc byte[2 + 8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(chnl[..2], Channels);
            "SLFT"u8.CopyTo(chnl[2..6]);
            "SRGT"u8.CopyTo(chnl[6..10]);
            WriteChunkTo(prop, "CHNL"u8, chnl.ToArray());

            var cmpr = new byte[8];
            "DSD "u8.CopyTo(cmpr.AsSpan(0, 4));
            WriteChunkTo(prop, "CMPR"u8, cmpr);

            Span<byte> lsco = stackalloc byte[2];
            System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(lsco, 6);
            WriteChunkTo(prop, "LSCO"u8, lsco.ToArray());
        }
        var propData = new byte[4 + (int)prop.Length];
        "SND "u8.CopyTo(propData.AsSpan(0, 4));
        prop.ToArray().CopyTo(propData, 4);
        WriteChunk(body, "PROP"u8, propData);

        WriteChunk(body, "DSD "u8, Enumerable.Repeat((byte)0x69, DsdBytes).ToArray());

        switch (c)
        {
            case FixtureCase.Id3Valid:
                WriteChunk(body, "ID3 "u8, new byte[32]); // zeros = valid-size ID3
                break;
            case FixtureCase.Id3CorruptSize:
            case FixtureCase.Id3CorruptPlusBracketed:
                WriteChunk(body, "ID3 "u8, [0xA0, 0x00, 0x00, 0x00]); // sync-safe-mangled size: 0xA0 read as 0x20
                break;
            case FixtureCase.ComtNonAscii:
                var text = Encoding.UTF8.GetBytes("ripped by é test");
                var comt = new byte[4 + 2 + text.Length];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(comt[..4], 0);
                System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(comt.AsSpan(4, 2), (short)text.Length);
                text.CopyTo(comt, 6);
                WriteChunk(body, "COMT"u8, comt);
                break;
            case FixtureCase.Baseline:
            case FixtureCase.BracketedName:
            default:
                break;
        }

        using var fs = File.Create(path);
        fs.Write("FRM8"u8);
        Span<byte> frmSize = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(frmSize, (ulong)(body.Length + 4)); // form type + chunks
        fs.Write(frmSize);
        fs.Write("DSD "u8);
        body.Position = 0;
        body.CopyTo(fs);

        return path;
    }

    private static void WriteChunkTo(MemoryStream target, ReadOnlySpan<byte> id, ReadOnlySpan<byte> data)
    {
        target.Write(id);
        Span<byte> size = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)data.Length);
        target.Write(size);
        target.Write(data);
        if (data.Length % 2 != 0) target.WriteByte(0);
    }

    public static long ExpectedPcmBytes() => (long)(Seconds * SampleRate / 32.0 * Channels * 3); // 264600 for 0.5s DSD64 stereo 24-bit 88.2k PCM
}
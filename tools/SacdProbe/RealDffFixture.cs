using System.Buffers.Binary;

namespace SacdProbe;

/// <summary>
/// Provides access to the real Disc 10 Karajan DFF for probe runs.
/// Replaces DffFixtureFactory for the real-media pivot (v2 spec §5).
///
/// Expected PCM output (DSD64 → 88.2kHz/24-bit):
///   DSD64 sample rate = 2822400 Hz
///   Decimation factor = 32 → PCM rate = 88200 Hz
///   2-channel, 3 bytes/sample (24-bit)
///   Duration ≈ DSD-bytes / (2822400/8 * channels) seconds
///   PCM = duration × 88200 × channels × 3
/// </summary>
internal static class RealDffFixture
{
    /// <summary>Full path to the real Disc 10 DFF (2.86 GB).</summary>
    public const string Path = @"C:\Temp\t.dff";

    private const int DsdSampleRate = 2822400;
    private const int Channels = 2;
    private const int PcmSampleRate = 88200;
    private const int BytesPerPcmSample = 3;

    public static bool Exists() => File.Exists(Path);

    /// <summary>
    /// Reads the DSD chunk size from the real DFF to compute the exact expected PCM byte count.
    /// Returns -1 if the chunk cannot be located.
    /// </summary>
    public static long ExpectedPcmBytes()
    {
        if (!File.Exists(Path)) return -1;
        var dsdBytes = ReadDsdChunkSize(Path);
        if (dsdBytes <= 0) return -1;

        var dsdSamplesPerChannel = dsdBytes / Channels;  // each sample = 1 bit; dsd bytes are interleaved
        var durationSeconds = (double)dsdSamplesPerChannel * 8.0 / DsdSampleRate;
        var pcmSamples = (long)(durationSeconds * PcmSampleRate);
        return pcmSamples * Channels * BytesPerPcmSample;
    }

    private static long ReadDsdChunkSize(string path)
    {
        // Walk top-level DSDIFF chunks to locate "DSD " data chunk
        using var fs = File.OpenRead(path);
        // Skip FRM8 header (4 id + 8 size + 4 form type = 16)
        fs.Seek(16, SeekOrigin.Begin);
        Span<byte> hdr = stackalloc byte[12];
        while (fs.Position < fs.Length - 12)
        {
            if (fs.Read(hdr) < 12) break;
            var id = System.Text.Encoding.ASCII.GetString(hdr[..4]);
            var size = BinaryPrimitives.ReadUInt64BigEndian(hdr[4..]);
            if (id == "DSD ")
                return (long)size;
            // Pad byte: DSDIFF chunks are even-padded
            var skip = size % 2 != 0 ? size + 1 : size;
            if (fs.Position + (long)skip > fs.Length) break;
            fs.Seek((long)skip, SeekOrigin.Current);
        }
        return -1;
    }
}

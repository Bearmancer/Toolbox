using System.Buffers.Binary;

namespace Services.Audio;

internal static class RealDffFixture
{
	public const string Path = @"C:\Temp\t.dff";

	private const int DsdSampleRate = 2822400;
	private const int Channels = 2;
	private const int PcmSampleRate = 88200;
	private const int BytesPerPcmSample = 3;

	public static bool Exists() => File.Exists(Path);

	public static long ExpectedPcmBytes()
	{
		if (!File.Exists(Path))
			return -1;
		var dsdBytes = ReadDsdChunkSize(Path);
		if (dsdBytes <= 0)
			return -1;

		var dsdSamplesPerChannel = dsdBytes / Channels;
		var durationSeconds = (double)dsdSamplesPerChannel * 8.0 / DsdSampleRate;
		var pcmSamples = (long)(durationSeconds * PcmSampleRate);
		return pcmSamples * Channels * BytesPerPcmSample;
	}

	private static long ReadDsdChunkSize(string path)
	{
		using FileStream fs = File.OpenRead(path);
		fs.Seek(16, SeekOrigin.Begin);
		Span<byte> hdr = stackalloc byte[12];
		while (fs.Position < fs.Length - 12)
		{
			if (fs.Read(hdr) < 12)
				break;
			var id = System.Text.Encoding.ASCII.GetString(hdr[..4]);
			var size = BinaryPrimitives.ReadUInt64BigEndian(hdr[4..]);
			if (id == "DSD ")
				return (long)size;
			var skip = size % 2 != 0 ? size + 1 : size;
			if (fs.Position + (long)skip > fs.Length)
				break;
			fs.Seek((long)skip, SeekOrigin.Current);
		}
		return -1;
	}
}

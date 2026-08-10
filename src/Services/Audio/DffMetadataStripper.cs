using System.Buffers.Binary;
using Core;

namespace Services.Audio;

using ErrorOr;

/// <summary>
/// Strips ID3v2 metadata chunks from DSDIFF (.dff) files.
/// Saracon's wxWidgets GUI throws "Cannot convert from the charset 'Unknown encoding (-1)'!"
/// when it encounters corrupted or non-standard ID3v2 encoding bytes embedded in DFF files.
/// </summary>
public static class DffMetadataStripper
{
	private const string Id3ChunkId = "ID3 ";

	public static bool HasId3Chunk(string dffPath)
	{
		if (!File.Exists(dffPath))
			return false;

		try
		{
			using var stream = File.OpenRead(dffPath);
			if (stream.Length < 12)
				return false;

			var magicBuf = new byte[4];
			stream.ReadExactly(magicBuf, 0, 4);
			if (System.Text.Encoding.ASCII.GetString(magicBuf) != "FRM8")
				return false;

			stream.Seek(12, SeekOrigin.Begin);

			while (stream.Position < stream.Length - 12)
			{
				var chunkIdBuf = new byte[4];
				stream.ReadExactly(chunkIdBuf, 0, 4);
				var chunkId = System.Text.Encoding.ASCII.GetString(chunkIdBuf);
				var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(stream, 8));

				if (chunkId == Id3ChunkId)
					return true;

				var skip = (long)chunkSize;
				if (skip % 2 != 0)
					skip++;
				stream.Seek(skip, SeekOrigin.Current);
			}
		}
		catch (Exception ex)
		{
			Telemetry.Warn("DffMetadataStripper.HasId3Chunk failed for {File}: {Error}", dffPath, ex.Message);
			return false;
		}

		return false;
	}

	public static async Task<ErrorOr<string>> StripId3TagsAsync(
		string dffPath,
		string outputDir,
		CancellationToken ct = default
	)
	{
		if (!HasId3Chunk(dffPath))
			return dffPath;

		var cleanPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(dffPath) + "_clean.dff");

		try
		{
			await using var input = File.OpenRead(dffPath);
			await using var output = File.Create(cleanPath);

			if (input.Length < 12)
				return Errors.Audio.ConversionFailed(dffPath, "File too small to be valid DSDIFF");

			await CopyBytes(input, output, 12, ct);

			while (input.Position < input.Length - 12)
			{
				var chunkIdBuf = new byte[4];
				await ReadExact(input, chunkIdBuf, ct);
				var chunkId = System.Text.Encoding.ASCII.GetString(chunkIdBuf);
				var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(ReadBytesFromStream(input, 8));

				if (chunkId != Id3ChunkId)
				{
					await WriteBytes(output, chunkIdBuf, ct);
					await WriteBytes(output, chunkSize, ct);
					await CopyBytes(input, output, (long)chunkSize, ct);

					if (chunkSize % 2 != 0 && input.Position < input.Length)
						await CopyBytes(input, output, 1, ct);
				}
				else
				{
					Telemetry.Debug("DffMetadataStripper.SkippedId3 size={Size}KB", chunkSize / 1024.0);
					var skip = (long)chunkSize;
					if (skip % 2 != 0)
						skip++;
					input.Seek(skip, SeekOrigin.Current);
				}
			}

			Telemetry.Debug("DffMetadataStripper.Complete input={Input} clean={Clean} size={Size}MB",
				Path.GetFileName(dffPath), Path.GetFileName(cleanPath), new FileInfo(cleanPath).Length / 1_048_576.0);

			return cleanPath;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Error("DffMetadataStripper.StripFailed file={File}: {Error}", dffPath, ex.Message);
			if (File.Exists(cleanPath))
				File.Delete(cleanPath);
			return Errors.Audio.ConversionFailed(dffPath, $"ID3 strip failed: {ex.Message}");
		}
	}

	private static async Task CopyBytes(Stream input, Stream output, long count, CancellationToken ct)
	{
		var buffer = new byte[81920];
		var remaining = count;
		while (remaining > 0)
		{
			ct.ThrowIfCancellationRequested();
			var toRead = (int)Math.Min(buffer.Length, remaining);
			var read = await input.ReadAsync(buffer.AsMemory(0, toRead), ct);
			if (read == 0)
				break;
			await output.WriteAsync(buffer.AsMemory(0, read), ct);
			remaining -= read;
		}
	}

	private static byte[] ReadBytes(Stream stream, int count)
	{
		var buf = new byte[count];
		stream.ReadExactly(buf, 0, count);
		return buf;
	}

	private static async Task ReadExact(Stream stream, byte[] buffer, CancellationToken ct)
	{
		var read = await stream.ReadAsync(buffer.AsMemory(), ct);
		if (read != buffer.Length)
			throw new IOException($"Expected {buffer.Length} bytes, got {read}");
	}

	private static async Task WriteBytes(Stream stream, byte[] data, CancellationToken ct) =>
		await stream.WriteAsync(data.AsMemory(), ct);

	private static async Task WriteBytes(Stream stream, ulong value, CancellationToken ct)
	{
		var buf = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(buf, value);
		await stream.WriteAsync(buf.AsMemory(), ct);
	}

	private static byte[] ReadBytesFromStream(Stream stream, int count)
	{
		var buf = new byte[count];
		stream.ReadExactly(buf, 0, count);
		return buf;
	}
}

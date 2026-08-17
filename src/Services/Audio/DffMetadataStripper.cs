using System.Buffers.Binary;
using System.Text;
using Core;

namespace Services.Audio;

using ErrorOr;

public static class DffMetadataStripper
{
	private const int HeaderSize = 12,
		DffHeaderSize = 16;
	private const string FormId = "FRM8",
		FormType = "DSD ",
		Id3ChunkId = "ID3 ",
		PropChunkId = "PROP";

	public static bool HasId3Chunk(string dffPath)
	{
		if (!File.Exists(dffPath))
			return false;

		try
		{
			using FileStream input = File.OpenRead(dffPath);
			return ScanAsync(input, CancellationToken.None).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			Telemetry.Error(
				"DffMetadataStripper.ScanFailed file={File} error={Error}",
				LogPaths.Format(dffPath),
				ex.Message
			);
			throw;
		}
	}

	public static async Task<ErrorOr<string>> StripId3TagsAsync(
		string dffPath,
		string outputDir,
		CancellationToken ct = default
	)
	{
		var cleanPath = Path.Combine(
			outputDir,
			Path.GetFileNameWithoutExtension(dffPath) + "_clean.dff"
		);
		var outputCreated = false;
		var completed = false;

		try
		{
			await using FileStream input = File.OpenRead(dffPath);
			var hasId3 = await ScanAsync(input, ct);
			if (!hasId3)
				return dffPath;

			Directory.CreateDirectory(outputDir);
			await using FileStream output = new(
				cleanPath,
				FileMode.Create,
				FileAccess.ReadWrite,
				FileShare.None,
				81920,
				FileOptions.Asynchronous | FileOptions.SequentialScan
			);
			outputCreated = true;

			input.Position = 0;
			var dffHeader = await ReadExactlyAsync(input, DffHeaderSize, ct);
			await output.WriteAsync(dffHeader, ct);
			input.Position = DffHeaderSize;
			await CopyChunksAsync(input, output, input.Length, ct);

			var outputDataSize = output.Length - HeaderSize;
			if ((outputDataSize & 1) != 0)
				throw new InvalidDataException("Filtered DFF length is not even");

			output.Position = 4;
			var sizeBytes = new byte[8];
			BinaryPrimitives.WriteUInt64BigEndian(sizeBytes, checked((ulong)outputDataSize));
			await output.WriteAsync(sizeBytes, ct);
			await output.FlushAsync(ct);

			output.Position = 4;
			var writtenSize = BinaryPrimitives.ReadUInt64BigEndian(
				await ReadExactlyAsync(output, 8, ct)
			);
			if (writtenSize != (ulong)outputDataSize)
				throw new InvalidDataException(
					"Filtered DFF FRM8 size does not match output length"
				);

			Telemetry.Debug(
				"DffMetadataStripper.Completed input={Input} clean={Clean} inputBytes={InputBytes} outputBytes={OutputBytes}",
				Path.GetFileName(dffPath),
				Path.GetFileName(cleanPath),
				input.Length,
				output.Length
			);
			completed = true;
			return cleanPath;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Error(
				"DffMetadataStripper.StripFailed file={File} error={Error}",
				LogPaths.Format(dffPath),
				ex.Message
			);
			return Errors.Audio.ConversionFailed(dffPath, $"ID3 strip failed: {ex.Message}");
		}
		finally
		{
			if (outputCreated && !completed)
			{
				try
				{
					File.Delete(cleanPath);
				}
				catch (Exception cleanupError)
				{
					Telemetry.Error(
						"DffMetadataStripper.CleanupFailed file={File} error={Error}",
						LogPaths.Format(cleanPath),
						cleanupError.Message
					);
				}
			}
		}
	}

	private static async Task<bool> ScanAsync(Stream input, CancellationToken ct)
	{
		if (input.Length < DffHeaderSize)
			throw new InvalidDataException("File too small to be valid DSDIFF");

		var header = await ReadExactlyAsync(input, DffHeaderSize, ct);
		ValidateDffHeader(header, input.Length);
		return await ScanChunksAsync(input, input.Length, ct);
	}

	private static async Task<bool> ScanChunksAsync(Stream input, long end, CancellationToken ct)
	{
		var found = false;
		while (input.Position < end)
		{
			Chunk chunk = await ReadChunkAsync(input, end, ct);
			if (chunk.Id == Id3ChunkId)
			{
				found = true;
				input.Position = chunk.End;
				continue;
			}

			if (chunk.Id == PropChunkId)
			{
				if (chunk.Size < 4)
					throw new InvalidDataException("PROP chunk is missing property type");

				input.Position += 4;
				found |= await ScanChunksAsync(input, chunk.DataEnd, ct);
			}

			input.Position = chunk.End;
		}

		if (input.Position != end)
			throw new InvalidDataException("DSDIFF chunk walk did not end on a chunk boundary");

		return found;
	}

	private static async Task CopyChunksAsync(
		Stream input,
		Stream output,
		long end,
		CancellationToken ct
	)
	{
		while (input.Position < end)
		{
			var headerPosition = input.Position;
			Chunk chunk = await ReadChunkAsync(input, end, ct);
			if (chunk.Id == Id3ChunkId)
			{
				Telemetry.Debug(
					"DffMetadataStripper.Id3ChunkSkipped offset={Offset} sizeBytes={SizeBytes}",
					headerPosition,
					chunk.Size
				);
				input.Position = chunk.End;
				continue;
			}

			if (chunk.Id != PropChunkId)
			{
				input.Position = headerPosition;
				await CopyBytesAsync(input, output, chunk.End - headerPosition, ct);
				continue;
			}

			if (chunk.Size < 4)
				throw new InvalidDataException("PROP chunk is missing property type");

			var outputHeaderPosition = output.Position;
			await WriteChunkHeaderAsync(output, chunk.Id, chunk.Size, ct);
			input.Position = chunk.DataStart;
			await CopyBytesAsync(input, output, 4, ct);
			await CopyChunksAsync(input, output, chunk.DataEnd, ct);
			var outputSize = output.Position - outputHeaderPosition - HeaderSize;
			if ((outputSize & 1) != 0)
				throw new InvalidDataException("Filtered PROP chunk length is not even");

			var endPosition = output.Position;
			output.Position = outputHeaderPosition + 4;
			var sizeBytes = new byte[8];
			BinaryPrimitives.WriteUInt64BigEndian(sizeBytes, checked((ulong)outputSize));
			await output.WriteAsync(sizeBytes, ct);
			output.Position = endPosition;
			input.Position = chunk.End;
		}

		if (input.Position != end)
			throw new InvalidDataException("DSDIFF chunk copy did not end on a chunk boundary");
	}

	private static async Task<Chunk> ReadChunkAsync(Stream input, long end, CancellationToken ct)
	{
		if (end - input.Position < HeaderSize)
			throw new InvalidDataException("DSDIFF chunk header is truncated");

		var header = await ReadExactlyAsync(input, HeaderSize, ct);
		var id = Encoding.ASCII.GetString(header, 0, 4);
		var size = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8));
		var dataEnd = checked(input.Position + (long)size);
		var endPosition = checked(dataEnd + (long)(size & 1));
		if (endPosition > end)
			throw new InvalidDataException($"DSDIFF chunk {id} exceeds its parent boundary");

		return new Chunk(id, size, input.Position, dataEnd, endPosition);
	}

	private static void ValidateDffHeader(byte[] header, long length)
	{
		if (Encoding.ASCII.GetString(header, 0, 4) != FormId)
			throw new InvalidDataException("DSDIFF file does not start with FRM8");
		if (Encoding.ASCII.GetString(header, 12, 4) != FormType)
			throw new InvalidDataException("DSDIFF FRM8 form type is not DSD");

		var declaredSize = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8));
		if (declaredSize != (ulong)(length - HeaderSize))
			throw new InvalidDataException("DSDIFF FRM8 size does not match source length");
	}

	private static async Task<byte[]> ReadExactlyAsync(
		Stream input,
		int count,
		CancellationToken ct
	)
	{
		var buffer = new byte[count];
		var offset = 0;
		while (offset < count)
		{
			var read = await input.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
			if (read == 0)
				throw new EndOfStreamException($"Expected {count} bytes, got {offset}");
			offset += read;
		}

		return buffer;
	}

	private static async Task CopyBytesAsync(
		Stream input,
		Stream output,
		long count,
		CancellationToken ct
	)
	{
		var buffer = new byte[81920];
		var remaining = count;
		while (remaining > 0)
		{
			var requested = (int)Math.Min(buffer.Length, remaining);
			var read = await input.ReadAsync(buffer.AsMemory(0, requested), ct);
			if (read == 0)
				throw new EndOfStreamException(
					$"Expected {count} bytes while copying, got {count - remaining}"
				);
			await output.WriteAsync(buffer.AsMemory(0, read), ct);
			remaining -= read;
		}
	}

	private static async Task WriteChunkHeaderAsync(
		Stream output,
		string id,
		ulong size,
		CancellationToken ct
	)
	{
		var header = new byte[HeaderSize];
		Encoding.ASCII.GetBytes(id, header.AsSpan(0, 4));
		BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(4, 8), size);
		await output.WriteAsync(header, ct);
	}

	private sealed record Chunk(string Id, ulong Size, long DataStart, long DataEnd, long End);
}

using System.Buffers.Binary;
using System.Text;
using Core;
using ErrorOr;

namespace Services.Audio;

public static class DffHeaderReader
{
	public static ErrorOr<DffHeader> Read(string dffPath)
	{
		try
		{
			using FileStream stream = File.OpenRead(dffPath);
			using BinaryReader reader = new(stream);

			var magic = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
			if (magic != "FRM8")
				return Errors.Audio.ProbeFailed(dffPath, $"Not a DSDIFF file (magic: {magic})");

			var formSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
			if (formSize < 4)
				throw new InvalidDataException("DSDIFF FORM is too small");
			var formEnd = checked(12 + checked((long)formSize));
			if (formEnd > stream.Length)
				throw new InvalidDataException("DSDIFF FORM exceeds stream bounds");
			var formType = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
			if (formType != "DSD ")
				return Errors.Audio.ProbeFailed(dffPath, $"Unexpected form type: {formType}");

			var sampleRate = 0;
			var channels = 0;

			while (stream.Position < formEnd)
			{
				if (formEnd - stream.Position < 12)
					throw new InvalidDataException("Truncated DSDIFF chunk header");

				var chunkId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
				var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
				var chunkEnd = checked(stream.Position + checked((long)chunkSize));
				if (chunkEnd > formEnd || chunkEnd > stream.Length)
					throw new InvalidDataException("DSDIFF chunk exceeds bounds");

				if (chunkId == "PROP")
				{
					if (chunkSize < 4)
						throw new InvalidDataException("PROP chunk is too small");

					var propEnd = chunkEnd;
					var propType = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
					if (propType == "SND ")
					{
						while (stream.Position < propEnd)
						{
							if (propEnd - stream.Position < 12)
								throw new InvalidDataException("Truncated PROP subchunk header");

							var subId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
							var subSize = BinaryPrimitives.ReadUInt64BigEndian(
								ReadExactBytes(reader, 8)
							);
							var subEnd = checked(stream.Position + checked((long)subSize));
							if (subEnd > propEnd)
								throw new InvalidDataException("PROP subchunk exceeds PROP chunk");

							if (subId == "FS  ")
							{
								if (subSize < 4)
									throw new InvalidDataException("FS subchunk is too small");

								sampleRate = checked(
									(int)
										BinaryPrimitives.ReadUInt32BigEndian(
											ReadExactBytes(reader, 4)
										)
								);
							}
							else if (subId == "CHNL")
							{
								if (subSize < 2)
									throw new InvalidDataException("CHNL subchunk is too small");

								channels = BinaryPrimitives.ReadUInt16BigEndian(
									ReadExactBytes(reader, 2)
								);
							}

							SeekChecked(stream, subEnd);
							if (subSize % 2 != 0)
							{
								var paddedSubEnd = checked(subEnd + 1);
								if (paddedSubEnd > propEnd)
									throw new InvalidDataException(
										"PROP subchunk padding exceeds PROP chunk"
									);
								SeekChecked(stream, paddedSubEnd);
							}
						}
					}
				}

				SeekChecked(stream, chunkEnd);
				if (chunkSize % 2 != 0)
				{
					var paddedChunkEnd = checked(chunkEnd + 1);
					if (paddedChunkEnd > formEnd || paddedChunkEnd > stream.Length)
						throw new InvalidDataException("DSDIFF chunk padding exceeds bounds");
					SeekChecked(stream, paddedChunkEnd);
				}

				if (sampleRate > 0 && channels > 0)
					break;
			}

			if (sampleRate == 0 || channels == 0)
				return Errors.Audio.ProbeFailed(
					dffPath,
					"Could not parse FS or CHNL chunks from DFF header"
				);

			return new DffHeader(sampleRate, channels);
		}
		catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
		{
			return Errors.Audio.ProbeFailed(dffPath, ex.Message);
		}
	}

	private static byte[] ReadExactBytes(BinaryReader reader, int count)
	{
		var bytes = reader.ReadBytes(count);
		if (bytes.Length != count)
			throw new EndOfStreamException();
		return bytes;
	}

	private static void SeekChecked(FileStream stream, long target)
	{
		if (target < 0 || target > stream.Length)
			throw new InvalidDataException("DSDIFF seek exceeds stream bounds");
		stream.Seek(target, SeekOrigin.Begin);
	}
}

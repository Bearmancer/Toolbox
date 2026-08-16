using System.Buffers.Binary;
using System.Text;
using ErrorOr;
using Services.Audio;

namespace ProbeVerify;

internal static class Program
{
	private static int _passed;
	private static int _failed;

	internal static async Task<int> Main()
	{
		await TestValidDff();
		await TestOversizedChunk();
		await TestOversizedPropSubchunk();
		await TestCorruptMagic();
		await TestTruncatedHeader();
		await TestPropMissingPropertyType();
		await TestNonSndPropSkipsCorrectly();

		Console.WriteLine();
		Console.WriteLine($"Results: {_passed} passed, {_failed} failed");
		return _failed > 0 ? 1 : 0;
	}

	private static async Task TestValidDff()
	{
		string path = WriteTempDff(BuildValidDff());
		try
		{
			DsdConvertService svc = CreateService();
			ErrorOr<DsdProbeResult> result = await svc.ProbeDsdAsync(path);
			Assert("ValidDff", result.IsSuccess, $"Expected success, got error: {result.Errors[0].Description}");
		}
		finally { File.Delete(path); }
	}

	private static async Task TestOversizedChunk()
	{
		string path = WriteTempDff(BuildDffWithOversizedChunk());
		try
		{
			DsdConvertService svc = CreateService();
			ErrorOr<DsdProbeResult> result = await svc.ProbeDsdAsync(path);
			Assert("OversizedChunk_ReturnsError", result.IsError, "Expected ErrorOr error for oversized chunk");
			Assert("OversizedChunk_NoThrow", true, "Did not throw (caught by ErrorOr)");
		}
		catch (Exception ex)
		{
			Assert("OversizedChunk_NoThrow", false, $"Threw: {ex.GetType().Name}: {ex.Message}");
		}
		finally { File.Delete(path); }
	}

	private static async Task TestOversizedPropSubchunk()
	{
		string path = WriteTempDff(BuildDffWithOversizedPropSubchunk());
		try
		{
			DsdConvertService svc = CreateService();
			ErrorOr<DsdProbeResult> result = await svc.ProbeDsdAsync(path);
			Assert("OversizedPropSubchunk_ReturnsError", result.IsError, "Expected ErrorOr error for oversized PROP subchunk");
		}
		catch (Exception ex)
		{
			Assert("OversizedPropSubchunk_NoThrow", false, $"Threw: {ex.GetType().Name}: {ex.Message}");
		}
		finally { File.Delete(path); }
	}

	private static async Task TestCorruptMagic()
	{
		string path = WriteTempDff(BuildDffWithCorruptMagic());
		try
		{
			DsdConvertService svc = CreateService();
			ErrorOr<DsdProbeResult> result = await svc.ProbeDsdAsync(path);
			Assert("CorruptMagic_ReturnsError", result.IsError, "Expected ErrorOr error for corrupt magic");
		}
		catch (Exception ex)
		{
			Assert("CorruptMagic_NoThrow", false, $"Threw: {ex.GetType().Name}: {ex.Message}");
		}
		finally { File.Delete(path); }
	}

	private static async Task TestTruncatedHeader()
	{
		string path = WriteTempDff(BuildTruncatedDff());
		try
		{
			DsdConvertService svc = CreateService();
			ErrorOr<DsdProbeResult> result = await svc.ProbeDsdAsync(path);
			Assert("TruncatedHeader_ReturnsError", result.IsError, "Expected ErrorOr error for truncated file");
		}
		catch (Exception ex)
		{
			Assert("TruncatedHeader_NoThrow", false, $"Threw: {ex.GetType().Name}: {ex.Message}");
		}
		finally { File.Delete(path); }
	}

	private static async Task TestPropMissingPropertyType()
	{
		string path = WriteTempDff(BuildDffWithTinyProp());
		try
		{
			DsdConvertService svc = CreateService();
			ErrorOr<DsdProbeResult> result = await svc.ProbeDsdAsync(path);
			Assert("TinyProp_ReturnsError", result.IsError, "Expected ErrorOr error for PROP with < 4 bytes");
		}
		catch (Exception ex)
		{
			Assert("TinyProp_NoThrow", false, $"Threw: {ex.GetType().Name}: {ex.Message}");
		}
		finally { File.Delete(path); }
	}

	private static async Task TestNonSndPropSkipsCorrectly()
	{
		string path = WriteTempDff(BuildDffWithNonSndProp());
		try
		{
			DsdConvertService svc = CreateService();
			ErrorOr<DsdProbeResult> result = await svc.ProbeDsdAsync(path);
			Assert("NonSndProp_IgnoresNonSnd", result.IsSuccess, "PROP with non-SND type should be skipped");
		}
		catch (Exception ex)
		{
			Assert("NonSndProp_NoThrow", false, $"Threw: {ex.GetType().Name}: {ex.Message}");
		}
		finally { File.Delete(path); }
	}

	private static DsdConvertService CreateService() =>
		new(null!, null!, new AudioMetadataService());

	private static string WriteTempDff(byte[] data)
	{
		string path = Path.Combine(Path.GetTempPath(), $"probe_test_{Guid.NewGuid():N}.dff");
		File.WriteAllBytes(path, data);
		return path;
	}

	private static void Assert(string name, bool condition, string detail)
	{
		if (condition)
		{
			Console.WriteLine($"  PASS  {name}");
			_passed++;
		}
		else
		{
			Console.WriteLine($"  FAIL  {name}: {detail}");
			_failed++;
		}
	}

	private static byte[] BuildValidDff()
	{
		using MemoryStream ms = new();
		using BinaryWriter w = new(ms, Encoding.ASCII, leaveOpen: true);

		w.Write(Encoding.ASCII.GetBytes("FRM8"));
		w.Write(EncodeU64BE(0));
		w.Write(Encoding.ASCII.GetBytes("DSD "));

		byte[] propData = BuildSndPropData();
		w.Write(Encoding.ASCII.GetBytes("PROP"));
		w.Write(EncodeU64BE((ulong)propData.Length));
		w.Write(propData);

		byte[] dsdData = new byte[16];
		w.Write(Encoding.ASCII.GetBytes("DSD "));
		w.Write(EncodeU64BE((ulong)dsdData.Length));
		w.Write(dsdData);

		long dataLen = ms.Position - 12;
		ms.Position = 4;
		w.Write(EncodeU64BE((ulong)dataLen));

		return ms.ToArray();
	}

	private static byte[] BuildSndPropData()
	{
		using MemoryStream ms = new();
		using BinaryWriter w = new(ms, Encoding.ASCII, leaveOpen: true);

		w.Write(Encoding.ASCII.GetBytes("SND "));

		w.Write(Encoding.ASCII.GetBytes("FS  "));
		w.Write(EncodeU64BE(4));
		w.Write(EncodeU32BE(2822400));

		w.Write(Encoding.ASCII.GetBytes("CHNL"));
		w.Write(EncodeU64BE(2));
		w.Write(EncodeU16BE(2));

		return ms.ToArray();
	}

	private static byte[] BuildDffWithOversizedChunk()
	{
		using MemoryStream ms = new();
		using BinaryWriter w = new(ms, Encoding.ASCII, leaveOpen: true);

		w.Write(Encoding.ASCII.GetBytes("FRM8"));
		w.Write(EncodeU64BE(0));
		w.Write(Encoding.ASCII.GetBytes("DSD "));

		w.Write(Encoding.ASCII.GetBytes("DATA"));
		w.Write(EncodeU64BE(10_000_000_000));

		return ms.ToArray();
	}

	private static byte[] BuildDffWithOversizedPropSubchunk()
	{
		using MemoryStream ms = new();
		using BinaryWriter w = new(ms, Encoding.ASCII, leaveOpen: true);

		w.Write(Encoding.ASCII.GetBytes("FRM8"));
		w.Write(EncodeU64BE(0));
		w.Write(Encoding.ASCII.GetBytes("DSD "));

		using MemoryStream propMs = new();
		using BinaryWriter propW = new(propMs, Encoding.ASCII, leaveOpen: true);
		propW.Write(Encoding.ASCII.GetBytes("SND "));
		propW.Write(Encoding.ASCII.GetBytes("FS  "));
		propW.Write(EncodeU64BE(10_000_000_000));
		byte[] propData = propMs.ToArray();

		w.Write(Encoding.ASCII.GetBytes("PROP"));
		w.Write(EncodeU64BE((ulong)propData.Length));
		w.Write(propData);

		return ms.ToArray();
	}

	private static byte[] BuildDffWithCorruptMagic()
	{
		byte[] data = new byte[20];
		Encoding.ASCII.GetBytes("NOT8").CopyTo(data, 0);
		EncodeU64BE(8).CopyTo(data, 4);
		Encoding.ASCII.GetBytes("DSD ").CopyTo(data, 12);
		return data;
	}

	private static byte[] BuildTruncatedDff()
	{
		byte[] data = new byte[4];
		Encoding.ASCII.GetBytes("FRM8").CopyTo(data, 0);
		return data;
	}

	private static byte[] BuildDffWithTinyProp()
	{
		using MemoryStream ms = new();
		using BinaryWriter w = new(ms, Encoding.ASCII, leaveOpen: true);

		w.Write(Encoding.ASCII.GetBytes("FRM8"));
		w.Write(EncodeU64BE(0));
		w.Write(Encoding.ASCII.GetBytes("DSD "));

		w.Write(Encoding.ASCII.GetBytes("PROP"));
		w.Write(EncodeU64BE(2));
		w.Write(new byte[] { 0x00, 0x01 });

		return ms.ToArray();
	}

	private static byte[] BuildDffWithNonSndProp()
	{
		using MemoryStream ms = new();
		using BinaryWriter w = new(ms, Encoding.ASCII, leaveOpen: true);

		w.Write(Encoding.ASCII.GetBytes("FRM8"));
		w.Write(EncodeU64BE(0));
		w.Write(Encoding.ASCII.GetBytes("DSD "));

		using MemoryStream propMs = new();
		using BinaryWriter propW = new(propMs, Encoding.ASCII, leaveOpen: true);
		propW.Write(Encoding.ASCII.GetBytes("NOT "));
		propW.Write(new byte[8]);
		byte[] propData = propMs.ToArray();

		w.Write(Encoding.ASCII.GetBytes("PROP"));
		w.Write(EncodeU64BE((ulong)propData.Length));
		w.Write(propData);

		byte[] sndPropData = BuildSndPropData();
		w.Write(Encoding.ASCII.GetBytes("PROP"));
		w.Write(EncodeU64BE((ulong)sndPropData.Length));
		w.Write(sndPropData);

		long dataLen = ms.Position - 12;
		ms.Position = 4;
		w.Write(EncodeU64BE((ulong)dataLen));

		return ms.ToArray();
	}

	private static byte[] EncodeU64BE(ulong value)
	{
		byte[] buf = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(buf, value);
		return buf;
	}

	private static byte[] EncodeU32BE(uint value)
	{
		byte[] buf = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(buf, value);
		return buf;
	}

	private static byte[] EncodeU16BE(ushort value)
	{
		byte[] buf = new byte[2];
		BinaryPrimitives.WriteUInt16BigEndian(buf, value);
		return buf;
	}
}

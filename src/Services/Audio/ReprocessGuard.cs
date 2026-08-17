using System.Text.Json;
using Core;

namespace Services.Audio;

public sealed class ReprocessGuard
{
	public const int MaxConsecutiveCount = 3;

	private static readonly string StatePath = Path.Combine(
		PathResolver.GetStatePath("audio"),
		"sacd-guard.json"
	);
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private readonly Dictionary<string, GuardEntry> Entries;

	private ReprocessGuard(Dictionary<string, GuardEntry> entries) => Entries = entries;

	public static async Task<ReprocessGuard> LoadAsync()
	{
		if (!File.Exists(StatePath))
			return new ReprocessGuard([]);

		await using FileStream stream = File.OpenRead(StatePath);
		Dictionary<string, GuardEntry>? entries =
			await JsonSerializer.DeserializeAsync<Dictionary<string, GuardEntry>>(
				stream,
				JsonOptions
			);
		return new ReprocessGuard(entries ?? []);
	}

	public GuardEntry? Get(string isoPath) => Entries.GetValueOrDefault(Path.GetFullPath(isoPath));

	public int GetCount(string isoPath) => Get(isoPath)?.ConsecutiveCount ?? 0;

	public async Task RecordAsync(string isoPath, DiscState verdict, CancellationToken ct = default)
	{
		isoPath = Path.GetFullPath(isoPath);

		if (verdict == DiscState.Complete)
		{
			if (Entries.TryGetValue(isoPath, out GuardEntry? prev))
			{
				Entries.Remove(isoPath);
				Telemetry.Warn(
					"Guard transition: {ISO} {PrevVerdict}({PrevCount}) → Complete(0)",
					isoPath,
					prev.Verdict,
					prev.ConsecutiveCount
				);
			}
			await SaveAsync(ct);
			return;
		}

		Entries.TryGetValue(isoPath, out GuardEntry? existing);
		DiscState? prevVerdict = existing?.Verdict;
		var prevCount = existing?.ConsecutiveCount ?? 0;
		var newCount = prevCount + 1;

		if (newCount >= MaxConsecutiveCount)
		{
			Entries[isoPath] = new GuardEntry(DiscState.Failed, newCount, DateTimeOffset.UtcNow);
			Telemetry.Warn(
				"Guard transition: {ISO} {PrevVerdict}({PrevCount}) → Failed({NewCount})",
				isoPath,
				prevVerdict?.ToString() ?? "none",
				prevCount,
				newCount
			);
		}
		else
		{
			Entries[isoPath] = new GuardEntry(verdict, newCount, DateTimeOffset.UtcNow);
			Telemetry.Warn(
				"Guard transition: {ISO} {PrevVerdict}({PrevCount}) → {NewVerdict}({NewCount})",
				isoPath,
				prevVerdict?.ToString() ?? "none",
				prevCount,
				verdict,
				newCount
			);
		}

		await SaveAsync(ct);
	}

	public async Task ResetAsync(string isoPath, CancellationToken ct = default)
	{
		isoPath = Path.GetFullPath(isoPath);
		if (Entries.TryGetValue(isoPath, out GuardEntry? entry))
		{
			Entries.Remove(isoPath);
			Telemetry.Warn(
				"Guard reset: {ISO} {Verdict}({Count})",
				isoPath,
				entry.Verdict,
				entry.ConsecutiveCount
			);
			await SaveAsync(ct);
		}
	}

	public async Task ResetAllAsync(CancellationToken ct = default)
	{
		foreach (KeyValuePair<string, GuardEntry> kv in Entries)
		{
			Telemetry.Warn(
				"Guard reset: {ISO} {Verdict}({Count})",
				kv.Key,
				kv.Value.Verdict,
				kv.Value.ConsecutiveCount
			);
		}
		Entries.Clear();
		await SaveAsync(ct);
	}

	public async Task SaveAsync(CancellationToken ct = default)
	{
		Directory.CreateDirectory(PathResolver.GetStatePath("audio"));

		var tempPath = StatePath + ".tmp";
		try
		{
			await using FileStream stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
			await JsonSerializer.SerializeAsync(stream, Entries, JsonOptions, ct);
			await stream.FlushAsync(ct);
			stream.Close();
			ct.ThrowIfCancellationRequested();
			File.Move(tempPath, StatePath, overwrite: true);
		}
		catch (IOException ex)
		{
			Telemetry.Error("Failed to save SACD guard to {Path}: {Error}", StatePath, ex.Message);
			throw;
		}
		catch (UnauthorizedAccessException ex)
		{
			Telemetry.Error(
				"Permission denied saving SACD guard to {Path}: {Error}",
				StatePath,
				ex.Message
			);
			throw;
		}
	}

	public sealed record GuardEntry(
		DiscState Verdict,
		int ConsecutiveCount,
		DateTimeOffset UpdatedAt
	);
}

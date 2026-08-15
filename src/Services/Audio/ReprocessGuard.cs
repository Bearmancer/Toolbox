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

		try
		{
			await using FileStream stream = File.OpenRead(StatePath);
			Dictionary<string, GuardEntry>? entries =
				await JsonSerializer.DeserializeAsync<Dictionary<string, GuardEntry>>(
					stream,
					JsonOptions
				);
			return new ReprocessGuard(entries ?? []);
		}
		catch (JsonException ex)
		{
			Telemetry.Warn("Corrupt SACD guard at {Path}, resetting: {Error}", StatePath, ex.Message);
			return new ReprocessGuard([]);
		}
		catch (IOException ex)
		{
			Telemetry.Error("Failed to load SACD guard from {Path}: {Error}", StatePath, ex.Message);
			throw;
		}
		catch (UnauthorizedAccessException ex)
		{
			Telemetry.Error(
				"Permission denied loading SACD guard from {Path}: {Error}",
				StatePath,
				ex.Message
			);
			throw;
		}
	}

	public GuardEntry? Get(string isoPath) => Entries.GetValueOrDefault(Path.GetFullPath(isoPath));

	public int GetCount(string isoPath) => Get(isoPath)?.ConsecutiveCount ?? 0;

	public async Task RecordAsync(string isoPath, DiscState verdict)
	{
		isoPath = Path.GetFullPath(isoPath);

		if (Entries.TryGetValue(isoPath, out GuardEntry? existing)
			&& existing.Verdict == DiscState.Failed)
			return;

		if (verdict == DiscState.Complete)
			Entries.Remove(isoPath);
		else
		{
			var count = existing?.Verdict == verdict ? existing.ConsecutiveCount + 1 : 1;
			Entries[isoPath] = count >= MaxConsecutiveCount
					? new GuardEntry(DiscState.Failed, count, DateTimeOffset.UtcNow)
					: new GuardEntry(verdict, count, DateTimeOffset.UtcNow);
		}

		await SaveAsync();
	}

	public async Task SaveAsync()
	{
		Directory.CreateDirectory(PathResolver.GetStatePath("audio"));

		try
		{
			await using FileStream stream = File.Create(StatePath);
			await JsonSerializer.SerializeAsync(stream, Entries, JsonOptions);
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

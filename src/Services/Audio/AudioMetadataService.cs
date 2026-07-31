using ATL;

namespace Services.Audio;

using ErrorOr;

public sealed class AudioMetadataService
{
	public ErrorOr<TrackMetadata> ReadDsdMetadata(string filePath)
	{
		try
		{
			var track = new Track(filePath);
			return new TrackMetadata(
				track.Title,
				track.Artist,
				track.Album,
				track.AlbumArtist,
				track.Year ?? 0,
				track.Genre,
				track.ISRC,
				track.CatalogNumber,
				track.TrackNumber ?? 0,
				track.TrackTotal ?? 0,
				track.DiscNumber ?? 0,
				track.DiscTotal ?? 0,
				track.Composer,
				track.Conductor
			);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return Error.Failure("Audio.MetadataReadFailed", ex.Message);
		}
	}

	public ErrorOr<Success> WriteFlacTags(string flacPath, TrackMetadata metadata)
	{
		try
		{
			var track = new Track(flacPath);

			if (!string.IsNullOrEmpty(metadata.Title))
				track.Title = metadata.Title;
			if (!string.IsNullOrEmpty(metadata.Artist))
				track.Artist = metadata.Artist;
			if (!string.IsNullOrEmpty(metadata.Album))
				track.Album = metadata.Album;
			if (!string.IsNullOrEmpty(metadata.AlbumArtist))
				track.AlbumArtist = metadata.AlbumArtist;
			if (metadata.Year > 0)
				track.Year = metadata.Year;
			if (!string.IsNullOrEmpty(metadata.Genre))
				track.Genre = metadata.Genre;
			if (!string.IsNullOrEmpty(metadata.Isrc))
				track.ISRC = metadata.Isrc;
			if (!string.IsNullOrEmpty(metadata.CatalogNumber))
				track.CatalogNumber = metadata.CatalogNumber;
			if (metadata.TrackNumber > 0)
				track.TrackNumber = metadata.TrackNumber;
			if (metadata.TrackTotal > 0)
				track.TrackTotal = metadata.TrackTotal;
			if (metadata.DiscNumber > 0)
				track.DiscNumber = metadata.DiscNumber;
			if (metadata.DiscTotal > 0)
				track.DiscTotal = metadata.DiscTotal;
			if (!string.IsNullOrEmpty(metadata.Composer))
				track.Composer = metadata.Composer;
			if (!string.IsNullOrEmpty(metadata.Conductor))
				track.Conductor = metadata.Conductor;

			track.Save();

			return Result.Success;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return Error.Failure("Audio.TagWriteFailed", ex.Message);
		}
	}

	public ErrorOr<Success> CopyMetadataFromCue(string flacPath, CueSheet cue, CueTrack track)
	{
		try
		{
			var t = new Track(flacPath);

			t.Title = track.Title;
			t.TrackNumber = track.TrackNumber;
			t.TrackTotal = cue.Tracks.Count;

			if (!string.IsNullOrEmpty(track.Performer))
				t.Artist = track.Performer;
			if (!string.IsNullOrEmpty(cue.AlbumTitle))
				t.Album = cue.AlbumTitle;
			if (!string.IsNullOrEmpty(cue.AlbumArtist))
				t.AlbumArtist = cue.AlbumArtist;
			if (!string.IsNullOrEmpty(cue.Genre))
				t.Genre = cue.Genre;
			if (!string.IsNullOrEmpty(cue.Date) && int.TryParse(cue.Date, out var year))
				t.Year = year;
			if (!string.IsNullOrEmpty(track.Isrc))
				t.ISRC = track.Isrc;

			t.Save();

			return Result.Success;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return Error.Failure("Audio.CueTagFailed", ex.Message);
		}
	}
}

public sealed record TrackMetadata(
	string? Title,
	string? Artist,
	string? Album,
	string? AlbumArtist,
	int Year,
	string? Genre,
	string? Isrc,
	string? CatalogNumber,
	int TrackNumber,
	int TrackTotal,
	int DiscNumber,
	int DiscTotal,
	string? Composer,
	string? Conductor
);

namespace Core;

public enum ServiceName
{
	LastFm,
	YouTube,
	Vision,
	Translate,
	TextAnalytics,
	Speech,
	DocIntel,
	Audio,
	Pristine,
	SdkDiagnostics,
}

public static class ServiceNameMethods
{
	extension(ServiceName s)
	{
		public string ToFileSlug() =>
			s switch
			{
				ServiceName.LastFm => "lastfm",
				ServiceName.YouTube => "youtube",
				ServiceName.Vision => "vision",
				ServiceName.Translate => "translate",
				ServiceName.TextAnalytics => "textanalytics",
				ServiceName.Speech => "speech",
				ServiceName.DocIntel => "docintel",
				ServiceName.Audio => "audio",
				ServiceName.Pristine => "pristine",
				ServiceName.SdkDiagnostics => "sdk",
				_ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
			};
	}
}

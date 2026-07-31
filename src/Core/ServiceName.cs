namespace Core;

public enum ServiceName
{
	LastFm,
	YouTube,
	OpenAi,
	Vision,
	Translate,
	TextAnalytics,
	Speech,
	DocIntel,
	Audio,
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
				ServiceName.OpenAi => "openai",
				ServiceName.Vision => "vision",
				ServiceName.Translate => "translate",
				ServiceName.TextAnalytics => "textanalytics",
				ServiceName.Speech => "speech",
				ServiceName.DocIntel => "docintel",
				ServiceName.Audio => "audio",
				_ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
			};
	}
}

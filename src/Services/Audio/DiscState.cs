namespace Services.Audio;

public enum DiscState
{
	Complete,
	NeedsPrimaryConversion,
	NeedsExtraction,
	InvalidArtifacts,
	Failed,
}

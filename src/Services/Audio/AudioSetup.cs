using Microsoft.Extensions.DependencyInjection;

namespace Services.Audio;

public static class AudioSetup
{
	extension(IServiceCollection services)
	{
		public void AddAudioServices()
		{
			ValidateBinaryOnPath("saracon");
			ValidateBinaryOnPath("sox");
			ValidateBinaryOnPath("sacd_extract");

			services.AddSingleton<ProcessRunner>();
			services.AddSingleton<PathValidator>();
			services.AddSingleton<DiskSpaceChecker>();
			services.AddSingleton(sp => new SacdExtractService(
				sp.GetRequiredService<ProcessRunner>(),
				"sacd_extract"
			));
			services.AddSingleton(sp => new SaraconService(
				sp.GetRequiredService<ProcessRunner>(),
				"saracon"
			));
			services.AddSingleton(sp => new SoxService(
				sp.GetRequiredService<ProcessRunner>(),
				"sox"
			));
			services.AddSingleton<DsdConvertService>();
			services.AddSingleton<AudioMetadataService>();
			services.AddSingleton<CueParser>();
			services.AddSingleton<PipelineOrchestrator>();
		}
	}

	private static void ValidateBinaryOnPath(string name)
	{
		if (!ProcessRunner.IsOnPath(name))
			throw new InvalidOperationException(
				$"{name} not found on PATH. Install it and ensure it is available in your system PATH."
			);
	}
}

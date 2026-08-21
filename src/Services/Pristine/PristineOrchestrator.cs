using Core;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Services.Audio;

namespace Services.Pristine;

public sealed class PristineOrchestrator(
	PristineBrowser browser,
	PristinePollService pollService,
	PristineApiClient api,
	PristineApiPollService apiPollService,
	FlacTranscodeService flacTranscode,
	IHttpClientFactory httpFactory
)
{
	public async Task<ErrorOr<List<PristineAlbumResult>>> DownloadAsync(
		string[]? codes,
		string? outDir,
		bool headless = false,
		CancellationToken ct = default
	)
	{
		if (codes is not { Length: > 0 })
		{
			Telemetry.Warn("Pristine.Orchestrator.NoCodes");
			return Errors.Pristine.NoCodesProvided;
		}

		var dest = PristineCredentials.ResolveOutDir(outDir);
		Directory.CreateDirectory(dest);

		var effective = codes;
		Telemetry.Info("Downloading {Count} album(s) → {Dest:l}", effective.Length, dest);
		Telemetry.Debug(
			"Pristine.Orchestrator.Start codes={Codes} headless={Headless}",
			string.Join(",", effective),
			headless
		);

		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);

		if (File.Exists(PristinePaths.AuthPath) is false)
		{
			Telemetry.Warn("Pristine.Orchestrator.AuthMissing path={Path}", PristinePaths.AuthPath);
			return Errors.Pristine.AuthMissing;
		}

		using HttpClient http = httpFactory.CreateClient();
		ErrorOr<string> apiKeyOr = await api.AuthenticateAsync(http, ct);
		var apiKey = apiKeyOr.IsError ? null : apiKeyOr.Value;
		if (apiKey is not null)
			Telemetry.Debug("Pristine.Orchestrator.ApiAuthOk");
		else
			Telemetry.Warn(
				"Pristine.Orchestrator.ApiAuthFailed err={Err} — every album falls back to the browser",
				apiKeyOr.FirstError.Description
			);

		IBrowserContext? ctx = null;
		try
		{
			List<PristineAlbumResult> results = [];
			foreach (var code in effective)
			{
				ct.ThrowIfCancellationRequested();
				Telemetry.Debug(
					"Pristine.Orchestrator.AlbumStart code={Code} {Index}/{Total}",
					code,
					results.Count + 1,
					effective.Length
				);

				(PristineAlbumResult r, ctx) = await DownloadAlbumAsync(
					apiKey,
					http,
					code,
					dest,
					headless,
					ctx,
					ct
				);
				r = await TranscodeAlbumAsync(r, ct);
				results.Add(r);
				var freshDownloaded = Math.Max(0, r.Downloaded - r.Resumed);
				var rejected = Math.Max(0, r.Expected - r.Downloaded);
				Telemetry.Info(
					"[{Index}/{Total}] {Code:l} \"{Title:l}\" — {Fresh} fresh, {Resumed} resumed, {Rejected} rejected → {Downloaded}/{Expected} tracks → {Out:l}",
					results.Count,
					effective.Length,
					r.Code,
					r.Title,
					freshDownloaded,
					r.Resumed,
					rejected,
					r.Downloaded,
					r.Expected,
					r.OutPath
				);
				if (effective.Length > 1)
					Telemetry.Info("────────────────────────────────────────");

				if (effective.Length > 1)
					await Task.Delay(3000, ct);
			}

			Telemetry.Info("Done — {Total} album(s) processed", results.Count);
			return results;
		}
		finally
		{
			if (ctx is not null)
				await ctx.DisposeAsync();
		}
	}

	private async Task<(PristineAlbumResult Result, IBrowserContext? Ctx)> DownloadAlbumAsync(
		string? apiKey,
		HttpClient http,
		string code,
		string dest,
		bool headless,
		IBrowserContext? ctx,
		CancellationToken ct
	)
	{
		if (apiKey is not null)
		{
			ErrorOr<PristineAlbumResult> apiResult = await apiPollService.DownloadSingleAlbumAsync(
				http,
				apiKey,
				code,
				dest,
				ct
			);
			if (apiResult.IsError is false)
				return (apiResult.Value, ctx);

			Telemetry.Warn(
				"Pristine.Orchestrator.ApiPathFailed code={Code} err={Err} — falling back to browser",
				code,
				apiResult.FirstError.Description
			);
		}

		try
		{
			ctx ??= await EnsureBrowserAsync(headless, ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Error("Pristine.Orchestrator.BrowserFailed: {Error}", ex.Message);
			return (
				new PristineAlbumResult
				{
					Code = code,
					Title = "error",
					OutPath = dest,
					Expected = 0,
					Downloaded = 0,
				},
				ctx
			);
		}

		if (ctx is null)
			return (
				new PristineAlbumResult
				{
					Code = code,
					Title = "error",
					OutPath = dest,
					Expected = 0,
					Downloaded = 0,
				},
				null
			);

		try
		{
			ErrorOr<PristineAlbumResult> pollResult = await pollService.DownloadSingleAlbumAsync(
				ctx,
				code,
				dest,
				http,
				ct
			);
			PristineAlbumResult r = pollResult.Match(
				value => value,
				errors => new PristineAlbumResult
				{
					Code = code,
					Title = "error",
					OutPath = dest,
					Expected = 0,
					Downloaded = 0,
				}
			);
			return (r, ctx);
		}
		catch (OperationCanceledException)
		{
			Telemetry.Debug("Pristine.Orchestrator.Cancelled code={Code}", code);
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Error(
				"Pristine.Orchestrator.AlbumFailed code={Code}: {Error}",
				code,
				ex.Message
			);
			return (
				new PristineAlbumResult
				{
					Code = code,
					Title = "error",
					OutPath = dest,
					Expected = 0,
					Downloaded = 0,
				},
				ctx
			);
		}
	}

	private async Task<PristineAlbumResult> TranscodeAlbumAsync(
		PristineAlbumResult r,
		CancellationToken ct
	)
	{
		if (r.Downloaded <= 0)
		{
			Telemetry.Info(
				"Transcode: skipped — 0/{Expected} tracks downloaded this run",
				r.Expected
			);
			return r;
		}

		if (Directory.Exists(r.OutPath) is false)
		{
			Telemetry.Warn(
				"Pristine.Orchestrator.TranscodeSkippedNoOutDir code={Code} path={Path}",
				r.Code,
				r.OutPath
			);
			return r;
		}

		ErrorOr<FlacTranscodeResult> transcodeOr;
		try
		{
			transcodeOr = await flacTranscode.TranscodeDirectoryAsync(r.OutPath, ct);
		}
		catch (TranscodePipelineException ex)
		{
			Telemetry.Error(
				"Pristine.Orchestrator.TranscodePipelineBroken code={Code}: {Error} — our own transcode output is broken, skipping this album's transcode, batch continues",
				r.Code,
				ex.Message
			);
			return r;
		}
		if (transcodeOr.IsError)
		{
			Telemetry.Warn(
				"Pristine.Orchestrator.TranscodeFailed code={Code} err={Err}",
				r.Code,
				transcodeOr.FirstError.Description
			);
			return r;
		}

		FlacTranscodeResult t = transcodeOr.Value;
		Telemetry.Debug(
			"Pristine.Orchestrator.Transcoded code={Code} converted={Converted} skipped={Skipped} failed={Failed}",
			r.Code,
			t.Converted,
			t.Skipped,
			t.Failed
		);
		if (t.Failed == 0)
			return r;

		return r with
		{
			Downloaded = Math.Max(0, r.Downloaded - t.Failed),
		};
	}

	private async Task<IBrowserContext?> EnsureBrowserAsync(bool headless, CancellationToken ct)
	{
		ErrorOr<IBrowserContext> ctxResult = await browser.CreateAsync(headless, ct);
		if (ctxResult.IsError)
			throw new InvalidOperationException(ctxResult.FirstError.Description);
		IBrowserContext ctx = ctxResult.Value;
		IPage seed = await ctx.NewPageAsync().WaitAsync(ct);
		try
		{
			await seed.GotoAsync(
					"https://pristinestreaming.com/app/browse",
					new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }
				)
				.WaitAsync(ct);
			Telemetry.Debug("Pristine.Orchestrator.SeedGotoOk");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Orchestrator.SeedGotoFailed: {Error}", ex.Message);
		}

		var loggedIn = await WaitForLoginAsync(seed, ct, 180);
		Telemetry.Debug("Pristine.Orchestrator.LoginCheck loggedIn={LoggedIn}", loggedIn);
		try
		{
			await seed.CloseAsync().WaitAsync(ct);
		}
		catch (OperationCanceledException)
		{
			Telemetry.Debug("Pristine.Orchestrator.SeedCloseCancelled");
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Orchestrator.SeedCloseFailed: {Error}", ex.Message);
		}

		if (loggedIn)
			return ctx;

		Telemetry.Warn("Pristine.Orchestrator.NotLoggedIn");
		await ctx.DisposeAsync();
		return null;
	}

	private static async Task<bool> WaitForLoginAsync(
		Microsoft.Playwright.IPage page,
		CancellationToken ct,
		int timeoutS = 180
	)
	{
		try
		{
			var url = page.Url;
			if (
				url.Contains("browse", StringComparison.OrdinalIgnoreCase)
				&& url.Contains("login", StringComparison.OrdinalIgnoreCase) is false
			)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Orchestrator.WaitLoginUrlCheckFailed: {Error}", ex.Message);
		}

		try
		{
			await page.WaitForURLAsync(
					"**pristinestreaming.com/app/browse**",
					new Microsoft.Playwright.PageWaitForURLOptions { Timeout = timeoutS * 1000 }
				)
				.WaitAsync(ct);
			return true;
		}
		catch (TimeoutException ex)
		{
			Telemetry.Warn(
				"Pristine.Orchestrator.WaitLoginTimeout {Timeout}s: {Error}",
				timeoutS,
				ex.Message
			);
			return false;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Debug("Pristine.Orchestrator.WaitLoginError: {Error}", ex.Message);
			return false;
		}
	}
}

using System.Text.RegularExpressions;
using Core;
using ErrorOr;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristineAlbumService(PristineDownloader downloader)
{
	private static readonly string PristineApp = "https://pristinestreaming.com/app/browse";
	private static readonly string S3Covers =
		"https://s3-eu-west-1.amazonaws.com/pristine-classical-storage/covers/";

	public async Task<ErrorOr<long?>> ResolveAlbumIdAsync(
		IPage page,
		string code,
		CancellationToken ct = default
	)
	{
		ct.ThrowIfCancellationRequested();
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		Telemetry.Debug("Pristine.Album.ResolveStart code={Code}", code);

		const string searchSelector = "#quick-search-input";
		const string resultSelector = ".result-heading + .album-grid button.album-open";
		using CancellationTokenSource resolveCts = CancellationTokenSource.CreateLinkedTokenSource(
			ct
		);
		resolveCts.CancelAfter(TimeSpan.FromSeconds(45));
		CancellationToken resolveCt = resolveCts.Token;

		for (var attempt = 0; attempt < 3; attempt++)
		{
			resolveCt.ThrowIfCancellationRequested();
			Telemetry.Debug(
				"Pristine.Album.Attempt code={Code} attempt={Attempt}/3",
				code,
				attempt + 1
			);

			try
			{
				await page.GotoAsync(
						PristineApp,
						new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }
					)
					.WaitAsync(resolveCt);

				try
				{
					await page.WaitForResponseAsync(
							resp =>
								resp.Url.Contains(
									"/api/v1/authenticate",
									StringComparison.OrdinalIgnoreCase
								),
							new PageWaitForResponseOptions { Timeout = 8000 }
						)
						.WaitAsync(resolveCt);
					Telemetry.Debug(
						"Pristine.Album.AuthenticateResponded code={Code} attempt={Attempt}",
						code,
						attempt + 1
					);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Telemetry.Debug(
						"Pristine.Album.AuthenticateWaitFailed code={Code} attempt={Attempt}: {Error}",
						code,
						attempt + 1,
						ex.Message
					);
				}

				ILocator search = page.Locator(searchSelector);

				bool searchAttached;
				try
				{
					await search
						.WaitForAsync(
							new LocatorWaitForOptions
							{
								State = WaitForSelectorState.Attached,
								Timeout = 5000,
							}
						)
						.WaitAsync(resolveCt);
					searchAttached = true;
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					searchAttached = false;
					Telemetry.Warn(
						"Pristine.Album.SearchMissing code={Code} attempt={Attempt}: {Error}",
						code,
						attempt + 1,
						ex.Message
					);
					(string Url, string Title, string Snippet) diag = await DumpPageAsync(
						page,
						resolveCt
					);
					Telemetry.Warn(
						"Pristine.Album.PageDiag code={Code} url={Url} title={Title} snippet={Snippet}",
						code,
						diag.Url,
						diag.Title,
						diag.Snippet
					);
				}

				if (searchAttached is false)
				{
					continue;
				}

				try
				{
					await search
						.FillAsync(code, new LocatorFillOptions { Timeout = 5000 })
						.WaitAsync(resolveCt);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Telemetry.Warn(
						"Pristine.Album.FillFailed code={Code} attempt={Attempt}: {Error}",
						code,
						attempt + 1,
						ex.Message
					);
					continue;
				}

				try
				{
					await page.RunAndWaitForResponseAsync(
							async () =>
								await search
									.PressAsync("Enter", new LocatorPressOptions { Timeout = 5000 })
									.WaitAsync(resolveCt),
							resp =>
								resp.Url.Contains(
									"/api/v1/search",
									StringComparison.OrdinalIgnoreCase
								),
							new PageRunAndWaitForResponseOptions { Timeout = 10000 }
						)
						.WaitAsync(resolveCt);
					Telemetry.Debug(
						"Pristine.Album.SearchApiResponded code={Code} attempt={Attempt}",
						code,
						attempt + 1
					);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Telemetry.Warn(
						"Pristine.Album.SearchApiWaitFailed code={Code} attempt={Attempt}: {Error}",
						code,
						attempt + 1,
						ex.Message
					);
				}

				ILocator result = page.Locator(resultSelector)
					.Filter(new LocatorFilterOptions { HasTextString = code });

				bool resultFound;
				try
				{
					await result
						.First.WaitForAsync(
							new LocatorWaitForOptions
							{
								State = WaitForSelectorState.Attached,
								Timeout = 10000,
							}
						)
						.WaitAsync(resolveCt);
					resultFound = true;
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					resultFound = false;
					Telemetry.Warn(
						"Pristine.Album.NoSearchResult code={Code} attempt={Attempt}: {Error}",
						code,
						attempt + 1,
						ex.Message
					);
					var resultCount = await page.Locator(resultSelector)
						.CountAsync()
						.WaitAsync(resolveCt);
					var gridCount = await page.Locator(".album-grid")
						.CountAsync()
						.WaitAsync(resolveCt);
					var headingCount = await page.Locator(".result-heading")
						.CountAsync()
						.WaitAsync(resolveCt);
					Telemetry.Warn(
						"Pristine.Album.NoSearchResultDiag code={Code} resultSelectorCount={ResultCount} albumGridCount={GridCount} resultHeadingCount={HeadingCount}",
						code,
						resultCount,
						gridCount,
						headingCount
					);
					(string Url, string Title, string Snippet) diag = await DumpPageAsync(
						page,
						resolveCt
					);
					Telemetry.Warn(
						"Pristine.Album.NoSearchResultPageDiag code={Code} url={Url} title={Title} snippet={Snippet}",
						code,
						diag.Url,
						diag.Title,
						diag.Snippet
					);
				}

				if (resultFound is false)
				{
					continue;
				}

				try
				{
					await result
						.First.ClickAsync(new LocatorClickOptions { Timeout = 5000 })
						.WaitAsync(resolveCt);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Telemetry.Warn(
						"Pristine.Album.ResultClickFailed code={Code} attempt={Attempt}: {Error}",
						code,
						attempt + 1,
						ex.Message
					);
					continue;
				}

				try
				{
					await page.WaitForURLAsync(
							new Regex(@"#album/\d+"),
							new PageWaitForURLOptions { Timeout = 8000 }
						)
						.WaitAsync(resolveCt);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Telemetry.Warn(
						"Pristine.Album.NavigateFailed code={Code} attempt={Attempt}: {Error}",
						code,
						attempt + 1,
						ex.Message
					);
					continue;
				}

				var currentUrl = page.Url;
				Telemetry.Debug(
					"Pristine.Album.CurrentUrl code={Code} url={Url}",
					code,
					currentUrl
				);

				Match match = Regex.Match(currentUrl, @"#album/(\d+)");
				if (
					match.Success is false
					|| long.TryParse(match.Groups[1].Value, out var id) is false
				)
				{
					Telemetry.Warn(
						"Pristine.Album.IdParseFailed url={Url} code={Code}",
						currentUrl,
						code
					);
					continue;
				}

				var title =
					await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Level = 1 })
						.TextContentAsync(new LocatorTextContentOptions { Timeout = 5000 })
						.WaitAsync(resolveCt)
					?? string.Empty;
				Telemetry.Debug("Pristine.Album.TitleRead code={Code} title={Title}", code, title);

				if (title.Contains(code, StringComparison.OrdinalIgnoreCase) is false)
				{
					Telemetry.Warn(
						"Pristine.Album.TitleMismatch code={Code} title={Title} url={Url}",
						code,
						title,
						currentUrl
					);
					continue;
				}

				Telemetry.Debug(
					"Pristine.Album.Resolved code={Code} id={Id} title={Title}",
					code,
					id,
					title
				);
				return id;
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn(
					"Pristine.Album.ResolveCancelled code={Code} attempt={Attempt}",
					code,
					attempt + 1
				);
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Warn(
					"Pristine.Album.ResolveFailed code={Code} attempt={Attempt}: {Error}",
					code,
					attempt + 1,
					ex.Message
				);
				try
				{
					await page.GotoAsync(
							PristineApp,
							new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }
						)
						.WaitAsync(resolveCt);
				}
				catch (Exception recoveryEx) when (recoveryEx is not OperationCanceledException)
				{
					Telemetry.Debug(
						"Pristine.Album.RecoveryGotoFailed code={Code}: {Error}",
						code,
						recoveryEx.Message
					);
				}
			}
		}

		Telemetry.Warn("Pristine.Album.ResolveFailed code={Code} attempts=3", code);
		return Errors.Pristine.ResolveFailed(code);
	}

	private static async Task<(string Url, string Title, string Snippet)> DumpPageAsync(
		IPage page,
		CancellationToken ct
	)
	{
		try
		{
			var url = page.Url;
			var title = await page.TitleAsync().WaitAsync(ct);
			var snippet =
				await page.EvaluateAsync<string>(
						"() => (document.body ? (document.body.innerText || '') : '').slice(0,500)"
					)
					.WaitAsync(ct)
				?? string.Empty;
			return (url, title, snippet.Replace("\n", " ").Replace("\r", " "));
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Album.DumpPageFailed: {Error}", ex.Message);
			return (page.Url, string.Empty, "dump-failed: " + ex.Message);
		}
	}

	public async Task StartPlaybackAsync(IPage page, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		Telemetry.Debug("Pristine.Album.StartPlayback");

		const string trackSelector = "li[data-album-track-id] button.track-title-button";

		try
		{
			await page.WaitForSelectorAsync(
					trackSelector,
					new PageWaitForSelectorOptions { Timeout = 15000 }
				)
				.WaitAsync(ct);
			Telemetry.Debug("Pristine.Album.TracklistVisible");
		}
		catch (OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Album.TracklistCancelled");
			throw;
		}
		catch (TimeoutException ex)
		{
			Telemetry.Warn("Pristine.Album.TracklistTimeout: {Error}", ex.Message);
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Album.TracklistWaitFailed: {Error}", ex.Message);
		}

		try
		{
			await page.Locator(trackSelector)
				.First.ClickAsync(new LocatorClickOptions { Timeout = 5000 })
				.WaitAsync(ct);
			Telemetry.Debug("Pristine.Album.PlaynowClickOk");
		}
		catch (OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Album.PlaynowClickCancelled");
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Warn("Pristine.Album.PlaynowClickFailed: {Error}", ex.Message);
		}

		try
		{
			await page.WaitForFunctionAsync(
					"() => !!document.querySelector('body > audio[src]')",
					null,
					new PageWaitForFunctionOptions { Timeout = 5000 }
				)
				.WaitAsync(ct);
			Telemetry.Debug("Pristine.Album.AudioSrcReady");
		}
		catch (OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Album.AudioSrcCancelled");
			throw;
		}
		catch (TimeoutException ex)
		{
			Telemetry.Debug("Pristine.Album.AudioSrcTimeout: {Error}", ex.Message);
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Album.AudioSrcWaitFailed: {Error}", ex.Message);
		}
	}

	public async Task<ErrorOr<List<string>>> ParseTracklistAsync(
		IPage page,
		CancellationToken ct = default
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		try
		{
			var raw = await page.EvaluateAsync<string[]>(
					"() => Array.from(document.querySelectorAll('li[data-album-track-id] button.track-title-button')).map(el=>el.textContent.trim())"
				)
				.WaitAsync(ct);
			List<string> list = raw is not null ? [.. raw] : [];
			Telemetry.Debug("Pristine.Album.ParseTracklist count={Count}", list.Count);
			return list;
		}
		catch (OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Album.ParseTracklistCancelled");
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Album.ParseTracklistFailed: {Error}", ex.Message);
			return Errors.Pristine.TracklistParseFailed(ex.Message);
		}
	}

	public async Task DownloadArtworkAndPdfAsync(
		IPage page,
		string albumOut,
		string albumTitle,
		HttpClient http,
		CancellationToken ct = default
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		var artworkSrc = string.Empty;
		try
		{
			artworkSrc =
				await page.EvaluateAsync<string>(
						"() => document.querySelector('main .cover img')?.src || ''"
					)
					.WaitAsync(ct)
				?? string.Empty;
			Telemetry.Debug(
				"Pristine.Album.ArtworkSrc src={Src}",
				artworkSrc.Length > 120 ? artworkSrc[..120] : artworkSrc
			);
		}
		catch (OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Album.ArtworkSrcCancelled");
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Album.ArtworkSrcFailed: {Error}", ex.Message);
		}

		if (string.IsNullOrEmpty(artworkSrc))
		{
			Telemetry.Debug("Pristine.Album.NoArtwork album={Album}", albumTitle);
			return;
		}

		var imgFile = artworkSrc.Split('/')[^1].Split('?')[0];
		var ext = Path.GetExtension(imgFile);
		if (string.IsNullOrEmpty(ext))
			ext = ".jpg";
		var nameNoExt = Path.GetFileNameWithoutExtension(imgFile);
		var imgDest = Path.Combine(albumOut, $"{albumTitle}{ext}");
		Telemetry.Debug(
			"Pristine.Album.ArtworkDownload src={Src} dest={Dest}",
			artworkSrc.Length > 80 ? artworkSrc[..80] : artworkSrc,
			Path.GetFileName(imgDest)
		);
		var imgOk = await downloader.DownloadAsync(artworkSrc, imgDest, http, ct);
		Telemetry.Debug(
			"Pristine.Album.ArtworkResult dest={Dest} ok={Ok}",
			Path.GetFileName(imgDest),
			imgOk
		);
		var pdfUrl = $"{S3Covers}{nameNoExt}.pdf";
		var pdfDest = Path.Combine(albumOut, $"{nameNoExt}.pdf");
		Telemetry.Debug(
			"Pristine.Album.PdfDownload url={Url} dest={Dest}",
			pdfUrl[..Math.Min(80, pdfUrl.Length)],
			Path.GetFileName(pdfDest)
		);
		var ok = await downloader.DownloadAsync(pdfUrl, pdfDest, http, ct);
		if (ok is false)
		{
			Telemetry.Debug(
				"Pristine.Album.PdfNotFound url={Url}",
				pdfUrl[..Math.Min(80, pdfUrl.Length)]
			);
			try
			{
				if (File.Exists(pdfDest))
					File.Delete(pdfDest);
			}
			catch (Exception ex)
			{
				Telemetry.Debug(
					"Pristine.Album.PdfDeleteFailed dest={Dest}: {Error}",
					Path.GetFileName(pdfDest),
					ex.Message
				);
			}
		}
		else
		{
			Telemetry.Debug("Pristine.Album.PdfOk dest={Dest}", Path.GetFileName(pdfDest));
		}
	}
}

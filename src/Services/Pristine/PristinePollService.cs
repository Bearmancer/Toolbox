using System.Globalization;
using Core;
using ErrorOr;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristinePollService(PristineAlbumService albumService, PristineDownloader downloader, PristineAudioVerifier verifier)
{
	private const int MaxStall = 60;
	private const int PostDlWaitMs = 2000;
	private const int PollMs = 1000;

	public async Task<PristineAlbumResult> DownloadSingleAlbumAsync(
		IBrowserContext ctx,
		string code,
		string outDir,
		HttpClient http,
		bool singleTrack = false,
		CancellationToken ct = default)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		Telemetry.Info("Pristine.Poll.Start code={Code} outDir={OutDir} single={Single}", code, outDir, singleTrack);
		IPage page = await ctx.NewPageAsync();
		try
		{
			Telemetry.Debug("Pristine.Poll.GotoBrowse code={Code}", code);
			try
			{
				await page.GotoAsync("https://pristinestreaming.com/app/browse", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(ct);
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn("Pristine.Poll.GotoBrowseCancelled code={Code}", code);
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Error("Pristine.Poll.GotoBrowseFailed code={Code}: {Error}", code, ex.Message);
				return new PristineAlbumResult { Code = code, Title = "unknown", OutPath = outDir, Expected = 0, Downloaded = 0 };
			}

			if (!await WaitForLoginAsync(page, ct))
			{
				Telemetry.Warn("Pristine.Poll.NotLoggedIn code={Code}", code);
				return new PristineAlbumResult { Code = code, Title = "unknown", OutPath = outDir, Expected = 0, Downloaded = 0 };
			}

			Telemetry.Debug("Pristine.Poll.Resolving code={Code}", code);
			ErrorOr<int?> albumIdOr = await albumService.ResolveAlbumIdAsync(page, code, ct);
			if (albumIdOr.IsError)
			{
				Telemetry.Warn("Pristine.Poll.ResolveFailed code={Code} err={Err}", code, albumIdOr.FirstError.Description);
				return new PristineAlbumResult { Code = code, Title = "unknown", OutPath = outDir, Expected = 0, Downloaded = 0 };
			}

			var albumId = albumIdOr.Value;
			if (albumId is null)
			{
				Telemetry.Warn("Pristine.Poll.ResolveNull code={Code}", code);
				return new PristineAlbumResult { Code = code, Title = "unknown", OutPath = outDir, Expected = 0, Downloaded = 0 };
			}

			Telemetry.Info("Pristine.Poll.Resolved code={Code} id={Id}", code, albumId);
			try
			{
				await page.GotoAsync($"https://pristinestreaming.com/app/browse/albums/{albumId}", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(ct);
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn("Pristine.Poll.GotoAlbumCancelled code={Code} id={Id}", code, albumId);
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Error("Pristine.Poll.GotoAlbumFailed code={Code} id={Id}: {Error}", code, albumId, ex.Message);
				return new PristineAlbumResult { Code = code, Title = "unknown", OutPath = outDir, Expected = 0, Downloaded = 0 };
			}

			try
			{
				await page.WaitForSelectorAsync(".pp-album-view__title", new PageWaitForSelectorOptions { Timeout = 30000 }).WaitAsync(ct);
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn("Pristine.Poll.TitleWaitCancelled code={Code}", code);
				throw;
			}
			catch (TimeoutException ex)
			{
				Telemetry.Warn("Pristine.Poll.TitleTimeout code={Code}: {Error}", code, ex.Message);
			}
			catch (Exception ex)
			{
				Telemetry.Debug("Pristine.Poll.TitleWaitError code={Code}: {Error}", code, ex.Message);
			}

			var rawTitle = "Unknown Album";
			try
			{
				var titleVal = await page.EvaluateAsync<string>("() => document.querySelector('.pp-album-view__title')?.textContent?.trim()||'Unknown Album'").WaitAsync(ct) ?? "Unknown Album";
				rawTitle = titleVal;
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn("Pristine.Poll.TitleEvalCancelled code={Code}", code);
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Warn("Pristine.Poll.TitleEvalFailed code={Code}: {Error}", code, ex.Message);
			}

			var albumTitle = PristineText.SanitizePathComponent(rawTitle);
			Telemetry.Info("Pristine.Poll.AlbumTitle code={Code} title={Title}", code, albumTitle);
			var albumOut = Path.Combine(outDir, albumTitle);
			Directory.CreateDirectory(albumOut);
			Telemetry.Debug("Pristine.Poll.OutDir code={Code} path={Path}", code, albumOut);

			ErrorOr<List<string>> tracklistResult = await albumService.ParseTracklistAsync(page, ct);
			List<string> expectedTracks = tracklistResult.Match(t => t, _ => []);
			var expectedCount = expectedTracks.Count;
			Telemetry.Info("Pristine.Poll.Expected code={Code} count={Count}", code, expectedCount);
			if (expectedTracks.Count > 0)
			{
				Telemetry.Debug("Pristine.Poll.Tracklist code={Code}: {Tracks}", code, string.Join(" | ", expectedTracks.Select((t, i) => $"[{i + 1:00}] {t}")));
			}
			else
			{
				Telemetry.Warn("Pristine.Poll.EmptyTracklist code={Code}", code);
			}

			try
			{
				await albumService.DownloadArtworkAndPdfAsync(page, albumOut, albumTitle, http, ct);
				Telemetry.Debug("Pristine.Poll.ArtworkDone code={Code}", code);
			}
			catch (Exception ex)
			{
				Telemetry.Warn("Pristine.Poll.ArtworkFailed code={Code}: {Error}", code, ex.Message);
			}

			List<string> capturedUrls = [];
			EventHandler<IRequest> handler = (_, req) =>
			{
				var url = req.Url;
				if (PristineText.IsAudioUrl(url) && capturedUrls.Contains(url) is false)
				{
					capturedUrls.Add(url);
					Telemetry.Debug("Pristine.Poll.Captured code={Code} url={Url}", code, url.Length > 120 ? url[..120] : url);
				}
			};
			page.Request += handler;

			Telemetry.Info("Pristine.Poll.StartPlayback code={Code}", code);
			try
			{
				await albumService.StartPlaybackAsync(page, ct);
			}
			catch (Exception ex)
			{
				Telemetry.Error("Pristine.Poll.PlaybackFailed code={Code}: {Error}", code, ex.Message);
			}

			try
			{
				await Task.Delay(4000, ct);
			}
			catch (OperationCanceledException)
			{
				throw;
			}

			List<string> observed = [.. capturedUrls];
			Telemetry.Info("Pristine.Poll.Probe code={Code} streams={Streams}", code, observed.Count);
			foreach (var u in observed)
			{
				Telemetry.Debug("Pristine.Poll.Observed code={Code} url={Url}", code, u.Length > 160 ? u[..160] : u);
				try
				{
					var ext = Path.GetExtension(u.Split('?')[0]);
					Telemetry.Debug("Pristine.Poll.StreamDetail code={Code} ext={Ext} urlId={Id}", code, ext, u.Split('/')[^1].Split('?')[0]);
				}
				catch (Exception ex)
				{
					Telemetry.Debug("Pristine.Poll.StreamDetailFailed code={Code}: {Error}", code, ex.Message);
				}
			}

			try
			{
				var qualityScript = """
					(() => {
					  const cands = Array.from(document.querySelectorAll('[data-quality], .pp-quality, button, select, [role="listbox"]'));
					  for (const el of cands) {
					    const t=(el.textContent||'').toLowerCase();
					    if (t.includes('16') || t.includes('flac') || t.includes('cd')) return {found:true, text:t.trim().slice(0,80), tag:el.tagName, cls:el.className||''};
					  }
					  const body=(document.body.innerText||'').toLowerCase();
					  for (const kw of ['16-bit','16 bit','cd quality','flac']) if (body.includes(kw)) return {found:true, text:kw, tag:'body', cls:''};
					  return {found:false};
					})()
					""";
				var qualityJson = await page.EvaluateAsync<string>("(" + qualityScript + ") ? JSON.stringify(" + qualityScript + ") : '{}'").WaitAsync(ct) ?? "{}";
				Telemetry.Debug("Pristine.Poll.QualityProbe code={Code} result={Result}", code, qualityJson.Length > 200 ? qualityJson[..200] : qualityJson);
				var clickScript = """
					(() => {
					  const els = Array.from(document.querySelectorAll('[data-quality], .pp-quality, button, select option, [role="option"], [role="listbox"] *'));
					  for (const el of els) { const t=(el.textContent||'').toLowerCase(); if (t.includes('16') || t.includes('flac')) { el.click(); return (el.textContent||'').trim().slice(0,80); } }
					  return '';
					})()
					""";
				var clickedQuality = await page.EvaluateAsync<string>(clickScript).WaitAsync(ct) ?? string.Empty;
				if (string.IsNullOrEmpty(clickedQuality) is false)
				{
					Telemetry.Info("Pristine.Poll.QualitySelected code={Code} quality={Quality}", code, clickedQuality);
					try
					{
						await Task.Delay(2000, ct);
					}
					catch (OperationCanceledException)
					{
						throw;
					}
				}
			}
			catch (Exception ex)
			{
				Telemetry.Debug("Pristine.Poll.QualityProbeFailed code={Code}: {Error}", code, ex.Message);
			}

			foreach (var u in capturedUrls.Where(u => observed.Contains(u) is false))
			{
				Telemetry.Debug("Pristine.Poll.ObservedPostQuality code={Code} url={Url}", code, u.Length > 160 ? u[..160] : u);
			}

			page.Request -= handler;

			List<string> candidates = [.. capturedUrls.Distinct()];
			HashSet<string> seenUrls = [];
			HashSet<string> seenTitles = [];
			var stall = 0;
			var trackNum = 0;
			List<(string Dest, bool Verified16)> results = [];

			Telemetry.Info("Pristine.Poll.LoopStart code={Code} maxStall={Max} candidates={Candidates} single={Single}", code, MaxStall, candidates.Count, singleTrack);
			using SemaphoreSlim gate = new(5);
			List<Task> pendingDownloads = [];

			while (stall < MaxStall)
			{
				ct.ThrowIfCancellationRequested();
				string? src = null;
				foreach (var c in candidates)
				{
					if (seenUrls.Contains(c) is false)
					{
						src = c;
						break;
					}
				}

				if (src is null)
				{
try
				{
					src = await page.EvaluateAsync<string?>("() => {var els=document.querySelectorAll('body > audio');for(var i=0;i<els.length;i++){var el=els[i];if(!el.paused&&el.hasAttribute('src'))return el.getAttribute('src');}return null;}").WaitAsync(ct);
				}
				catch (OperationCanceledException)
				{
					Telemetry.Warn("Pristine.Poll.ActiveSrcCancelled code={Code}", code);
					throw;
				}
				catch (Exception ex)
					{
						Telemetry.Debug("Pristine.Poll.ActiveSrcEvalFailed code={Code}: {Error}", code, ex.Message);
					}
				}

				if (src is not null && seenUrls.Contains(src) is false)
				{
					seenUrls.Add(src);
					stall = 0;
					trackNum++;
					var rawTrack = string.Empty;
					try
					{
						var trackVal = await page.EvaluateAsync<string>("() => document.querySelector('.pp-playbar__now-playing__track')?.textContent?.trim()||''").WaitAsync(ct) ?? string.Empty;
						rawTrack = trackVal;
					}
					catch (OperationCanceledException)
					{
						Telemetry.Warn("Pristine.Poll.TrackTitleCancelled code={Code}", code);
						throw;
					}
					catch (Exception ex)
					{
						Telemetry.Debug("Pristine.Poll.TrackTitleEvalFailed code={Code} track={Track}: {Error}", code, trackNum, ex.Message);
					}

					if (string.IsNullOrWhiteSpace(rawTrack))
						rawTrack = $"Track {trackNum:00}";

					if (seenTitles.Contains(rawTrack))
					{
						Telemetry.Info("Pristine.Poll.DuplicateTitle code={Code} title={Title} — all done", code, rawTrack);
						break;
					}

					seenTitles.Add(rawTrack);
					var normalized = PristineText.NormalizeTrackTitle(rawTrack);
					var safe = PristineText.SanitizePathComponent(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized));
					var ext = src.Contains(".flac", StringComparison.OrdinalIgnoreCase) ? ".flac" : ".mp3";
					var dest = Path.Combine(albumOut, $"{trackNum:00}. {safe}{ext}");
					Telemetry.Info("Pristine.Poll.Track code={Code} [{Num:00}] {Title}{Ext} -> {Dest}", code, trackNum, safe, ext, Path.GetFileName(dest));
					Telemetry.Debug("Pristine.Poll.Src code={Code} id={Id}", code, src.Split('/')[^1].Split('?')[0]);

					try
					{
						await page.EvaluateAsync("() => document.querySelectorAll('body > audio').forEach(e=>e.pause())").WaitAsync(ct);
					}
					catch (OperationCanceledException)
					{
						Telemetry.Warn("Pristine.Poll.PauseCancelled code={Code}", code);
						throw;
					}
					catch (Exception ex)
					{
						Telemetry.Debug("Pristine.Poll.PauseFailed code={Code}: {Error}", code, ex.Message);
					}

					var capturedSrc = src;
					var capturedDest = dest;
					var capturedTrack = trackNum;
					var capturedSafe = safe;
					Task downloadTask;
					if (singleTrack)
					{
						var dlOk = await downloader.DownloadAsync(capturedSrc, capturedDest, http, ct);
						if (dlOk is false)
						{
							Telemetry.Warn("Pristine.Poll.DownloadFailed code={Code} dest={Dest}", code, capturedDest);
						}
						else
						{
							Telemetry.Info("Pristine.Poll.DownloadOk code={Code} dest={Dest}", code, capturedDest);
							PristineProbeResult probe = await verifier.VerifyAsync(capturedDest, code, capturedTrack, ct);
							Telemetry.Info("Pristine.Poll.Verify code={Code} track={Track} is16={Is16} codec={Codec} dest={Dest} note={Note}", code, capturedTrack, probe.Is16BitFlac, probe.Codec, Path.GetFileName(capturedDest), probe.Note);
							results.Add((capturedDest, probe.Is16BitFlac));
						}

						Telemetry.Info("Pristine.Poll.SingleTrackDone code={Code}", code);
						break;
					}
					else
					{
						await gate.WaitAsync(ct);
						downloadTask = Task.Run(async () =>
						{
							try
							{
								var dlOk = await downloader.DownloadAsync(capturedSrc, capturedDest, http, ct);
								if (dlOk is false)
								{
									Telemetry.Warn("Pristine.Poll.DownloadFailed code={Code} dest={Dest}", code, capturedDest);
								}
								else
								{
									Telemetry.Info("Pristine.Poll.DownloadOk code={Code} dest={Dest}", code, capturedDest);
									PristineProbeResult probe = await verifier.VerifyAsync(capturedDest, code, capturedTrack, ct);
									Telemetry.Info("Pristine.Poll.Verify code={Code} track={Track} is16={Is16} codec={Codec} dest={Dest} note={Note}", code, capturedTrack, probe.Is16BitFlac, probe.Codec, Path.GetFileName(capturedDest), probe.Note);
									lock (results)
									{
										results.Add((capturedDest, probe.Is16BitFlac));
									}
								}
							}
							finally
							{
								gate.Release();
							}
						}, ct);
						pendingDownloads.Add(downloadTask);
					}

					if (singleTrack)
					{
						break;
					}

					if (expectedCount > 0 && trackNum >= expectedCount)
					{
						Telemetry.Info("Pristine.Poll.AllExpectedDone code={Code} {Done}/{Expected}", code, trackNum, expectedCount);
						break;
					}

					await Task.Delay(PostDlWaitMs, ct);
					try
					{
						await page.EvaluateAsync("var f=document.querySelector('.pp-play-controls__main__primary > li:nth-child(3) > button');if(f)f.click();").WaitAsync(ct);
						Telemetry.Debug("Pristine.Poll.ClickedForward code={Code}", code);
					}
					catch (OperationCanceledException)
					{
						Telemetry.Warn("Pristine.Poll.ForwardCancelled code={Code}", code);
						throw;
					}
					catch (Exception ex)
					{
						Telemetry.Debug("Pristine.Poll.ForwardFailed code={Code}: {Error}", code, ex.Message);
					}

					try
					{
						await page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('body > audio')).some(a => a.hasAttribute('src') && a.readyState >= 2)", null, new PageWaitForFunctionOptions { Timeout = 4000 }).WaitAsync(ct);
					}
					catch (OperationCanceledException)
					{
						Telemetry.Warn("Pristine.Poll.WaitReadyCancelled code={Code}", code);
						throw;
					}
					catch (TimeoutException)
					{
						Telemetry.Debug("Pristine.Poll.WaitReadyTimeout code={Code}", code);
					}
					catch (Exception ex)
					{
						Telemetry.Debug("Pristine.Poll.WaitReadyError code={Code}: {Error}", code, ex.Message);
					}

					try
					{
						await page.EvaluateAsync("var p=document.querySelector('.pp-play-controls__main__primary > li:nth-child(2) > button');if(p)p.click();").WaitAsync(ct);
					}
					catch (OperationCanceledException)
					{
						Telemetry.Warn("Pristine.Poll.PlayCancelled code={Code}", code);
						throw;
					}
					catch (Exception ex)
					{
						Telemetry.Debug("Pristine.Poll.PlayFailed code={Code}: {Error}", code, ex.Message);
					}

					try
					{
						await page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('body > audio')).some(a => !a.paused)", null, new PageWaitForFunctionOptions { Timeout = 3000 }).WaitAsync(ct);
					}
					catch (OperationCanceledException)
					{
						Telemetry.Warn("Pristine.Poll.WaitPlayingCancelled code={Code}", code);
						throw;
					}
					catch (TimeoutException)
					{
						Telemetry.Debug("Pristine.Poll.WaitPlayingTimeout code={Code}", code);
					}
					catch (Exception ex)
					{
						Telemetry.Debug("Pristine.Poll.WaitPlayingError code={Code}: {Error}", code, ex.Message);
					}
				}
				else
				{
					stall++;
					Telemetry.Debug("Pristine.Poll.Stall code={Code} {Stall}/{Max}", code, stall, MaxStall);
					var hasReady = false;
					try
					{
						hasReady = await page.EvaluateAsync<bool>("() => {var els=document.querySelectorAll('body > audio');for(var i=0;i<els.length;i++){var el=els[i];if(el.paused&&el.readyState>=4&&el.hasAttribute('src'))return true;}return false;}").WaitAsync(ct);
					}
					catch (OperationCanceledException)
					{
						Telemetry.Warn("Pristine.Poll.ReadyCheckCancelled code={Code}", code);
						throw;
					}
					catch (Exception ex)
					{
						Telemetry.Debug("Pristine.Poll.ReadyCheckFailed code={Code}: {Error}", code, ex.Message);
					}

					if (hasReady)
					{
						Telemetry.Debug("Pristine.Poll.ReadyPausedRetryingPlay code={Code}", code);
						try
						{
							await page.EvaluateAsync("var p=document.querySelector('.pp-play-controls__main__primary > li:nth-child(2) > button');if(p)p.click();").WaitAsync(ct);
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception ex)
						{
							Telemetry.Debug("Pristine.Poll.RetryPlayFailed code={Code}: {Error}", code, ex.Message);
						}
					}
					else if (stall == 5)
					{
						Telemetry.Debug("Pristine.Poll.Stall5RetryPlaynow code={Code}", code);
						try
						{
							await page.EvaluateAsync("var btn=document.querySelector('.pp-tracklist__item .pp-tracklist__item__playnow');if(btn){btn.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true}));}").WaitAsync(ct);
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception ex)
						{
							Telemetry.Debug("Pristine.Poll.StallRetryFailed code={Code}: {Error}", code, ex.Message);
						}
					}

					await Task.Delay(PollMs, ct);
				}
			}

			if (pendingDownloads.Count > 0)
			{
				Telemetry.Info("Pristine.Poll.AwaitPending code={Code} pending={Count}", code, pendingDownloads.Count);
				try
				{
					await Task.WhenAll(pendingDownloads);
				}
				catch (Exception ex)
				{
					Telemetry.Warn("Pristine.Poll.PendingFailed code={Code}: {Error}", code, ex.Message);
				}
			}

			if (results.Count > 0)
			{
				var flac16 = results.Count(r => r.Verified16);
				Telemetry.Info("Pristine.Poll.VerifySummary code={Code} total={Total} flac16={Flac16}", code, results.Count, flac16);
			}

			if (stall >= MaxStall)
			{
				Telemetry.Warn("Pristine.Poll.StallLimitReached code={Code} stall={Stall}", code, stall);
			}

			await Task.Delay(2000, ct);
			Telemetry.Info("Pristine.Poll.Done code={Code} downloaded={Downloaded} expected={Expected} out={Out}", code, seenTitles.Count, expectedCount, albumOut);
			return new PristineAlbumResult { Code = code, Title = albumTitle, OutPath = albumOut, Expected = expectedCount, Downloaded = seenTitles.Count };
		}
		finally
		{
			try
			{
				await page.CloseAsync().WaitAsync(ct);
				Telemetry.Debug("Pristine.Poll.PageClosed code={Code}", code);
			}
			catch (OperationCanceledException)
			{
				Telemetry.Debug("Pristine.Poll.CloseCancelled code={Code}", code);
			}
			catch (Exception ex)
			{
				Telemetry.Debug("Pristine.Poll.CloseFailed code={Code}: {Error}", code, ex.Message);
			}
		}
	}

	private static async Task<bool> WaitForLoginAsync(IPage page, CancellationToken ct, int timeoutS = 180)
	{
		try
		{
			if (page.Url.Contains("browse", StringComparison.OrdinalIgnoreCase) && page.Url.Contains("login", StringComparison.OrdinalIgnoreCase) is false)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Poll.LoginCheckFailed: {Error}", ex.Message);
		}

		try
		{
			await page.WaitForURLAsync("**pristinestreaming.com/app/browse**", new PageWaitForURLOptions { Timeout = timeoutS * 1000 }).WaitAsync(ct);
			return true;
		}
		catch (OperationCanceledException)
		{
			Telemetry.Debug("Pristine.Poll.LoginWaitCancelled");
			throw;
		}
		catch (TimeoutException ex)
		{
			Telemetry.Warn("Pristine.Poll.LoginTimeout {Timeout}s: {Error}", timeoutS, ex.Message);
			return false;
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Poll.LoginWaitError: {Error}", ex.Message);
			return false;
		}
	}
}

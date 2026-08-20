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

		// Preflight: verification is fail-closed (see VerifyAndKeepAsync), so if ffprobe isn't
		// available at all, every downloaded track would be rejected and deleted anyway. Check
		// this up front, before spending any bandwidth on navigation, artwork, or track
		// downloads against the user's paid account, rather than discovering it only after a
		// full album has already been pulled down. Uses the exact same availability check as
		// the per-file verifier (PristineAudioVerifier.IsFfprobeAvailable) so the two never
		// disagree about whether ffprobe is present.
		if (PristineAudioVerifier.IsFfprobeAvailable() is false)
		{
			Telemetry.Error("Pristine.Poll.NoFfprobePreflight code={Code} — ffprobe missing, refusing to start downloads: {Err}", code, Errors.Pristine.FfprobeMissing.Description);
			return new PristineAlbumResult { Code = code, Title = "unknown", OutPath = outDir, Expected = 0, Downloaded = 0 };
		}

		IPage page = await ctx.NewPageAsync().WaitAsync(ct);
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
			catch (OperationCanceledException)
			{
				throw;
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
			catch (OperationCanceledException)
			{
				throw;
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

					// Best-effort confirmation only — NOT a pre-download gate. There is no way to
					// know a stream's real bit depth before requesting and ffprobe-ing the file
					// (see PristineAudioVerifier.VerifyAsync, which is the authoritative, fail-closed
					// check). This just tells us whether the click appears to have registered, so a
					// silently-ignored click doesn't look identical to a confirmed one in the logs.
					var confirmScript = """
						(() => {
						  const cands = Array.from(document.querySelectorAll('[data-quality], .pp-quality, button, select option, [role="option"], [role="listbox"] *'));
						  for (const el of cands) {
						    const t=(el.textContent||'').toLowerCase();
						    if (t.includes('16') || t.includes('flac') || t.includes('cd')) {
						      if (el.getAttribute('aria-selected')==='true' || el.classList.contains('selected') || el.classList.contains('active') || el.selected===true) return true;
						    }
						  }
						  const body=(document.body.innerText||'').toLowerCase();
						  return ['16-bit','16 bit','cd quality'].some(kw => body.includes(kw));
						})()
						""";
					bool confirmed;
					try
					{
						confirmed = await page.EvaluateAsync<bool>(confirmScript).WaitAsync(ct);
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex)
					{
						confirmed = false;
						Telemetry.Debug("Pristine.Poll.QualityConfirmFailed code={Code}: {Error}", code, ex.Message);
					}

					if (confirmed)
						Telemetry.Info("Pristine.Poll.QualityConfirmed code={Code} quality={Quality}", code, clickedQuality);
					else
						Telemetry.Warn("Pristine.Poll.QualityUnconfirmed code={Code} quality={Quality} — click fired but page state doesn't show it selected; downloaded files remain subject to post-download ffprobe verification", code, clickedQuality);
				}
				else
				{
					Telemetry.Warn("Pristine.Poll.QualitySelectorNotFound code={Code} — no 16-bit/FLAC/CD-quality selector found on page; proceeding with default stream, relying on post-download ffprobe verification", code);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
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
			// Only destinations that both downloaded successfully AND passed post-download
			// ffprobe verification (fail-closed: rejected files are deleted, see
			// VerifyAndKeepAsync) — this is the authoritative "actually downloaded" count,
			// not merely "a track was observed in the playlist."
			List<string> results = [];

			Telemetry.Info("Pristine.Poll.LoopStart code={Code} maxStall={Max} candidates={Candidates} single={Single}", code, MaxStall, candidates.Count, singleTrack);
			using SemaphoreSlim gate = new(5);
			List<Task> pendingDownloads = [];

			// The loop and its post-loop drain of pendingDownloads are wrapped in try/finally so
			// the drain always runs before `gate` goes out of scope and is disposed — even when
			// the loop exits via ct.ThrowIfCancellationRequested(), a cancelled Task.Delay(...,
			// ct), or any of the OperationCanceledException rethrows below. Without this, in-flight
			// download tasks could still be running when `gate` is disposed, throwing
			// ObjectDisposedException from their own `finally { gate.Release(); }` (unobserved),
			// and could leave a partially-downloaded, unverified file on disk — violating the
			// fail-closed guarantee documented on VerifyAndKeepAsync.
			try
			{
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
							if (await VerifyAndKeepAsync(capturedDest, code, capturedTrack, ct))
								results.Add(capturedDest);
						}

						Telemetry.Info("Pristine.Poll.SingleTrackDone code={Code}", code);
						break;
					}
					else
					{
						await gate.WaitAsync(ct);
						// Deliberately passing CancellationToken.None here rather than `ct`: if ct
						// were already cancelled by the time Task.Run schedules this work, Task.Run
						// would return an already-cancelled task WITHOUT ever invoking the delegate
						// below — which would mean the `finally { gate.Release(); }` never runs,
						// leaking the permit just acquired by gate.WaitAsync(ct) above. Cancellation
						// is still honored via the ct passed to the awaited calls inside the delegate.
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
									if (await VerifyAndKeepAsync(capturedDest, code, capturedTrack, ct))
									{
										lock (results)
										{
											results.Add(capturedDest);
										}
									}
								}
							}
							finally
							{
								gate.Release();
							}
						}, CancellationToken.None);
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
			}
			finally
			{
				// Best-effort drain only — must never itself throw, or it would replace whatever
				// exception (e.g. the original OperationCanceledException) is already propagating
				// out of the try above. Individual task failures are already logged inside each
				// download task's own catch blocks; this only needs to guarantee every task is
				// observed before `gate` is disposed.
				if (pendingDownloads.Count > 0)
				{
					Telemetry.Info("Pristine.Poll.AwaitPending code={Code} pending={Count}", code, pendingDownloads.Count);
					try
					{
						await Task.WhenAll(pendingDownloads).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						Telemetry.Warn("Pristine.Poll.PendingDrainFailed code={Code}: {Error}", code, ex.Message);
					}
				}
			}

			if (trackNum > 0)
			{
				Telemetry.Info("Pristine.Poll.VerifySummary code={Code} attempted={Attempted} verified16={Verified}", code, trackNum, results.Count);
			}

			if (stall >= MaxStall)
			{
				Telemetry.Warn("Pristine.Poll.StallLimitReached code={Code} stall={Stall}", code, stall);
			}

			await Task.Delay(2000, ct);
			// A single-track run only ever intends to download one track, regardless of how
			// many tracks the full album has — so the "expected" count for pass/fail purposes
			// must be 1, not the full tracklist size. Using expectedCount here would make a
			// fully successful --single run look like a failed partial-album download (see
			// PristineDownloadCommand's Downloaded < Expected check).
			var finalExpected = singleTrack ? 1 : expectedCount;
			Telemetry.Info("Pristine.Poll.Done code={Code} downloaded={Downloaded} expected={Expected} out={Out}", code, results.Count, finalExpected, albumOut);
			return new PristineAlbumResult { Code = code, Title = albumTitle, OutPath = albumOut, Expected = finalExpected, Downloaded = results.Count };
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

	/// <summary>
	/// Authoritative, fail-closed post-download verification. There is no reliable
	/// pre-download signal for stream bit depth (see the quality-selector click above,
	/// which is best-effort UI automation, not a gate) — so this is where a non-16-bit
	/// FLAC file is actually rejected: verification failure or a hard verifier error
	/// (e.g. ffprobe missing) both delete the file and exclude the track from the
	/// album's Downloaded count, rather than silently keeping a file that doesn't meet
	/// the requirement.
	/// </summary>
	private async Task<bool> VerifyAndKeepAsync(string dest, string code, int track, CancellationToken ct)
	{
		ErrorOr<PristineProbeResult> probeOr = await verifier.VerifyAsync(dest, code, track, ct);
		if (probeOr.IsError)
		{
			Telemetry.Error("Pristine.Poll.VerifyHardFail code={Code} track={Track} dest={Dest} err={Err}", code, track, Path.GetFileName(dest), probeOr.FirstError.Description);
			DeleteRejectedFile(dest, code, track, probeOr.FirstError.Code);
			return false;
		}

		PristineProbeResult probe = probeOr.Value;
		Telemetry.Info("Pristine.Poll.Verify code={Code} track={Track} is16={Is16} codec={Codec} dest={Dest} note={Note}", code, track, probe.Is16BitFlac, probe.Codec, Path.GetFileName(dest), probe.Note);
		if (probe.Is16BitFlac is false)
		{
			DeleteRejectedFile(dest, code, track, probe.Note);
			return false;
		}

		return true;
	}

	private static void DeleteRejectedFile(string path, string code, int track, string reason)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
			Telemetry.Warn("Pristine.Poll.VerifyRejected code={Code} track={Track} dest={Dest} reason={Reason} — file deleted, track not counted as downloaded", code, track, Path.GetFileName(path), reason);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Warn("Pristine.Poll.VerifyRejectedDeleteFailed code={Code} track={Track} dest={Dest} reason={Reason}: {Error}", code, track, Path.GetFileName(path), reason, ex.Message);
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

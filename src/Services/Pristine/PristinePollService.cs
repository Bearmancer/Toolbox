using System.Globalization;
using Core;
using ErrorOr;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristinePollService(
	PristineAlbumService albumService,
	PristineDownloader downloader,
	PristineAudioVerifier verifier
)
{
	private const int MaxStall = 60;
	private const int PostDlWaitMs = 2000;
	private const int PollMs = 1000;

	public async Task<PristineAlbumResult> DownloadSingleAlbumAsync(
		IBrowserContext ctx,
		string code,
		string outDir,
		HttpClient http,
		CancellationToken ct = default
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		Telemetry.Debug("Pristine.Poll.Start code={Code} outDir={OutDir}", code, outDir);

		if (PristineAudioVerifier.IsFfprobeAvailable() is false)
		{
			Telemetry.Error(
				"Pristine.Poll.NoFfprobePreflight code={Code} — ffprobe missing, refusing to start downloads: {Err}",
				code,
				Errors.Pristine.FfprobeMissing.Description
			);
			return new PristineAlbumResult
			{
				Code = code,
				Title = "unknown",
				OutPath = outDir,
				Expected = 0,
				Downloaded = 0,
			};
		}

		IPage page = await ctx.NewPageAsync().WaitAsync(ct);
		try
		{
			Telemetry.Debug("Pristine.Poll.GotoBrowse code={Code}", code);
			try
			{
				await page.GotoAsync(
						"https://pristinestreaming.com/app/browse",
						new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }
					)
					.WaitAsync(ct);
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn("Pristine.Poll.GotoBrowseCancelled code={Code}", code);
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Error(
					"Pristine.Poll.GotoBrowseFailed code={Code}: {Error}",
					code,
					ex.Message
				);
				return new PristineAlbumResult
				{
					Code = code,
					Title = "unknown",
					OutPath = outDir,
					Expected = 0,
					Downloaded = 0,
				};
			}

			if (!await WaitForLoginAsync(page, ct))
			{
				Telemetry.Warn("Pristine.Poll.NotLoggedIn code={Code}", code);
				return new PristineAlbumResult
				{
					Code = code,
					Title = "unknown",
					OutPath = outDir,
					Expected = 0,
					Downloaded = 0,
				};
			}

			Telemetry.Debug("Pristine.Poll.Resolving code={Code}", code);
			ErrorOr<long?> albumIdOr = await albumService.ResolveAlbumIdAsync(page, code, ct);
			if (albumIdOr.IsError)
			{
				Telemetry.Warn(
					"Pristine.Poll.ResolveFailed code={Code} err={Err}",
					code,
					albumIdOr.FirstError.Description
				);
				return new PristineAlbumResult
				{
					Code = code,
					Title = "unknown",
					OutPath = outDir,
					Expected = 0,
					Downloaded = 0,
				};
			}

			var albumId = albumIdOr.Value;
			if (albumId is null)
			{
				Telemetry.Warn("Pristine.Poll.ResolveNull code={Code}", code);
				return new PristineAlbumResult
				{
					Code = code,
					Title = "unknown",
					OutPath = outDir,
					Expected = 0,
					Downloaded = 0,
				};
			}

			Telemetry.Debug("Pristine.Poll.Resolved code={Code} id={Id}", code, albumId);
			try
			{
				await page.GotoAsync(
						$"https://pristinestreaming.com/app/browse#album/{albumId}",
						new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }
					)
					.WaitAsync(ct);
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn(
					"Pristine.Poll.GotoAlbumCancelled code={Code} id={Id}",
					code,
					albumId
				);
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Error(
					"Pristine.Poll.GotoAlbumFailed code={Code} id={Id}: {Error}",
					code,
					albumId,
					ex.Message
				);
				return new PristineAlbumResult
				{
					Code = code,
					Title = "unknown",
					OutPath = outDir,
					Expected = 0,
					Downloaded = 0,
				};
			}

			ILocator titleHeading = page.GetByRole(
					AriaRole.Heading,
					new PageGetByRoleOptions { Level = 1 }
				)
				.Filter(new LocatorFilterOptions { HasTextString = code });
			var rawTitle = "Unknown Album";
			try
			{
				await titleHeading
					.WaitForAsync(
						new LocatorWaitForOptions
						{
							State = WaitForSelectorState.Attached,
							Timeout = 30000,
						}
					)
					.WaitAsync(ct);
				rawTitle =
					await titleHeading
						.TextContentAsync(new LocatorTextContentOptions { Timeout = 5000 })
						.WaitAsync(ct)
					?? "Unknown Album";
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
				Telemetry.Warn(
					"Pristine.Poll.TitleEvalFailed code={Code}: {Error}",
					code,
					ex.Message
				);
			}

			var albumTitle = PristineText.SanitizePathComponent(rawTitle);
			var folderName = PristineText.FormatAlbumFolderName(code, rawTitle);
			Telemetry.Debug(
				"Pristine.Poll.AlbumTitle code={Code} title={Title} folder={Folder}",
				code,
				albumTitle,
				folderName
			);
			var albumOut = Path.Combine(outDir, folderName);
			Directory.CreateDirectory(albumOut);
			Telemetry.Debug("Pristine.Poll.OutDir code={Code} path={Path}", code, albumOut);

			ErrorOr<List<string>> tracklistResult = await albumService.ParseTracklistAsync(
				page,
				ct
			);
			List<string> expectedTracks = tracklistResult.Match(t => t, _ => []);
			var expectedCount = expectedTracks.Count;
			Telemetry.Debug(
				"Pristine.Poll.Expected code={Code} count={Count}",
				code,
				expectedCount
			);
			if (expectedTracks.Count > 0)
			{
				Telemetry.Debug(
					"Pristine.Poll.Tracklist code={Code}: {Tracks}",
					code,
					string.Join(" | ", expectedTracks.Select((t, i) => $"[{i + 1:00}] {t}"))
				);
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
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Telemetry.Warn(
					"Pristine.Poll.ArtworkFailed code={Code}: {Error}",
					code,
					ex.Message
				);
			}

			List<string> capturedUrls = [];
			EventHandler<IRequest> handler = (_, req) =>
			{
				var url = req.Url;
				if (PristineText.IsAudioUrl(url) && capturedUrls.Contains(url) is false)
				{
					capturedUrls.Add(url);
					Telemetry.Debug(
						"Pristine.Poll.Captured code={Code} url={Url}",
						code,
						url.Length > 120 ? url[..120] : url
					);
				}
			};
			page.Request += handler;

			Telemetry.Debug("Pristine.Poll.StartPlayback code={Code}", code);
			try
			{
				await albumService.StartPlaybackAsync(page, ct);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Telemetry.Error(
					"Pristine.Poll.PlaybackFailed code={Code}: {Error}",
					code,
					ex.Message
				);
			}

			await Task.Delay(4000, ct);

			List<string> observed = [.. capturedUrls];
			Telemetry.Debug(
				"Pristine.Poll.Probe code={Code} streams={Streams}",
				code,
				observed.Count
			);
			foreach (var u in observed)
			{
				Telemetry.Debug(
					"Pristine.Poll.Observed code={Code} url={Url}",
					code,
					u.Length > 160 ? u[..160] : u
				);
				try
				{
					var ext = Path.GetExtension(u.Split('?')[0]);
					Telemetry.Debug(
						"Pristine.Poll.StreamDetail code={Code} ext={Ext} urlId={Id}",
						code,
						ext,
						u.Split('/')[^1].Split('?')[0]
					);
				}
				catch (Exception ex)
				{
					Telemetry.Debug(
						"Pristine.Poll.StreamDetailFailed code={Code}: {Error}",
						code,
						ex.Message
					);
				}
			}

			try
			{
				var currentFormat =
					await page.EvaluateAsync<string>(
							"() => document.querySelector('#mobile-playback-format')?.textContent?.trim()||''"
						)
						.WaitAsync(ct)
					?? string.Empty;
				Telemetry.Debug(
					"Pristine.Poll.PlaybackFormat code={Code} format={Format}",
					code,
					string.IsNullOrEmpty(currentFormat) ? "unknown" : currentFormat
				);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Telemetry.Debug(
					"Pristine.Poll.PlaybackFormatCheckFailed code={Code}: {Error}",
					code,
					ex.Message
				);
			}

			foreach (var u in capturedUrls.Where(u => observed.Contains(u) is false))
			{
				Telemetry.Debug(
					"Pristine.Poll.ObservedPostQuality code={Code} url={Url}",
					code,
					u.Length > 160 ? u[..160] : u
				);
			}

			page.Request -= handler;

			List<string> candidates = [.. capturedUrls.Distinct()];
			HashSet<string> seenUrls = [];
			HashSet<string> seenTitles = [];
			var stall = 0;
			var trackNum = 0;
			List<string> results = [];

			Telemetry.Debug(
				"Pristine.Poll.LoopStart code={Code} maxStall={Max} candidates={Candidates}",
				code,
				MaxStall,
				candidates.Count
			);
			using SemaphoreSlim gate = new(5);
			List<Task> pendingDownloads = [];

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
							src = await page.EvaluateAsync<string?>(
									"() => {var els=document.querySelectorAll('body > audio');for(var i=0;i<els.length;i++){var el=els[i];if(!el.paused&&el.hasAttribute('src'))return el.getAttribute('src');}return null;}"
								)
								.WaitAsync(ct);
						}
						catch (OperationCanceledException)
						{
							Telemetry.Warn("Pristine.Poll.ActiveSrcCancelled code={Code}", code);
							throw;
						}
						catch (Exception ex)
						{
							Telemetry.Debug(
								"Pristine.Poll.ActiveSrcEvalFailed code={Code}: {Error}",
								code,
								ex.Message
							);
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
							var trackVal =
								await page.EvaluateAsync<string>(
										"() => document.querySelector('#now-playing-title')?.textContent?.trim()||''"
									)
									.WaitAsync(ct)
								?? string.Empty;
							rawTrack = trackVal;
						}
						catch (OperationCanceledException)
						{
							Telemetry.Warn("Pristine.Poll.TrackTitleCancelled code={Code}", code);
							throw;
						}
						catch (Exception ex)
						{
							Telemetry.Debug(
								"Pristine.Poll.TrackTitleEvalFailed code={Code} track={Track}: {Error}",
								code,
								trackNum,
								ex.Message
							);
						}

						if (string.IsNullOrWhiteSpace(rawTrack))
							rawTrack = $"Track {trackNum:00}";

						if (seenTitles.Contains(rawTrack))
						{
							Telemetry.Debug(
								"Pristine.Poll.DuplicateTitle code={Code} title={Title} — all done",
								code,
								rawTrack
							);
							break;
						}

						seenTitles.Add(rawTrack);
						var normalized = PristineText.NormalizeTrackTitle(rawTrack);
						var safe = PristineText.SanitizePathComponent(
							CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized)
						);
						var ext = src.Contains(".flac", StringComparison.OrdinalIgnoreCase)
							? ".flac"
							: ".mp3";
						var stem = PristineText.ClampFileName(
							albumOut,
							$"{trackNum:00}. {safe}",
							ext
						);
						var dest = Path.Combine(albumOut, $"{stem}{ext}");
						if (File.Exists(dest) && new FileInfo(dest).Length > 0)
						{
							ErrorOr<PristineProbeResult> resumeProbeOr = await verifier.VerifyAsync(
								dest,
								code,
								trackNum,
								ct
							);
							var usable =
								resumeProbeOr.IsError is false
								&& resumeProbeOr.Value.Codec.Equals(
									"flac",
									StringComparison.OrdinalIgnoreCase
								)
								&& resumeProbeOr.Value.Bits is 16 or 24;
							if (usable)
							{
								Telemetry.Info(
									"  [{Num:00}] {Title} — already present, skipping",
									trackNum,
									safe
								);
								lock (results)
								{
									results.Add(dest);
								}
								continue;
							}

							PristineVerification.DeleteRejectedFile(
								dest,
								code,
								trackNum,
								resumeProbeOr.IsError
									? resumeProbeOr.FirstError.Description
									: $"resume-invalid bits={resumeProbeOr.Value.Bits}"
							);
						}

						Telemetry.Debug(
							"Pristine.Poll.Track code={Code} [{Num:00}] {Title}{Ext} -> {Dest}",
							code,
							trackNum,
							safe,
							ext,
							Path.GetFileName(dest)
						);
						Telemetry.Debug(
							"Pristine.Poll.Src code={Code} id={Id}",
							code,
							src.Split('/')[^1].Split('?')[0]
						);

						try
						{
							await page.EvaluateAsync(
									"() => document.querySelectorAll('body > audio').forEach(e=>e.pause())"
								)
								.WaitAsync(ct);
						}
						catch (OperationCanceledException)
						{
							Telemetry.Warn("Pristine.Poll.PauseCancelled code={Code}", code);
							throw;
						}
						catch (Exception ex)
						{
							Telemetry.Debug(
								"Pristine.Poll.PauseFailed code={Code}: {Error}",
								code,
								ex.Message
							);
						}

						var capturedSrc = src;
						var capturedDest = dest;
						var capturedTrack = trackNum;
						var capturedSafe = safe;
						await gate.WaitAsync(ct);
						Task downloadTask = Task.Run(
							async () =>
							{
								try
								{
									var dlOk = await downloader.DownloadAsync(
										capturedSrc,
										capturedDest,
										http,
										ct
									);
									if (dlOk is false)
									{
										Telemetry.Warn(
											"Pristine.Poll.DownloadFailed code={Code} dest={Dest}",
											code,
											capturedDest
										);
									}
									else
									{
										Telemetry.Debug(
											"Pristine.Poll.DownloadOk code={Code} dest={Dest}",
											code,
											capturedDest
										);
										if (
											await PristineVerification.VerifyAndKeepAsync(
												verifier,
												capturedDest,
												code,
												capturedTrack,
												ct
											)
										)
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
							},
							CancellationToken.None
						);
						pendingDownloads.Add(downloadTask);

						if (expectedCount > 0 && trackNum >= expectedCount)
						{
							Telemetry.Debug(
								"Pristine.Poll.AllExpectedDone code={Code} {Done}/{Expected}",
								code,
								trackNum,
								expectedCount
							);
							break;
						}

						await Task.Delay(PostDlWaitMs, ct);
						try
						{
							await page.GetByRole(
									AriaRole.Button,
									new PageGetByRoleOptions
									{
										NameString = "Next track",
										Exact = true,
									}
								)
								.ClickAsync(new LocatorClickOptions { Timeout = 3000 })
								.WaitAsync(ct);
							Telemetry.Debug("Pristine.Poll.ClickedForward code={Code}", code);
						}
						catch (OperationCanceledException)
						{
							Telemetry.Warn("Pristine.Poll.ForwardCancelled code={Code}", code);
							throw;
						}
						catch (Exception ex)
						{
							Telemetry.Debug(
								"Pristine.Poll.ForwardFailed code={Code}: {Error}",
								code,
								ex.Message
							);
						}

						try
						{
							await page.WaitForFunctionAsync(
									"() => Array.from(document.querySelectorAll('body > audio')).some(a => a.hasAttribute('src') && a.readyState >= 2)",
									null,
									new PageWaitForFunctionOptions { Timeout = 4000 }
								)
								.WaitAsync(ct);
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
							Telemetry.Debug(
								"Pristine.Poll.WaitReadyError code={Code}: {Error}",
								code,
								ex.Message
							);
						}

						try
						{
							await page.GetByRole(
									AriaRole.Button,
									new PageGetByRoleOptions { NameString = "Play", Exact = true }
								)
								.ClickAsync(new LocatorClickOptions { Timeout = 3000 })
								.WaitAsync(ct);
						}
						catch (OperationCanceledException)
						{
							Telemetry.Warn("Pristine.Poll.PlayCancelled code={Code}", code);
							throw;
						}
						catch (Exception ex)
						{
							Telemetry.Debug(
								"Pristine.Poll.PlayFailed code={Code}: {Error}",
								code,
								ex.Message
							);
						}

						try
						{
							await page.WaitForFunctionAsync(
									"() => Array.from(document.querySelectorAll('body > audio')).some(a => !a.paused)",
									null,
									new PageWaitForFunctionOptions { Timeout = 3000 }
								)
								.WaitAsync(ct);
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
							Telemetry.Debug(
								"Pristine.Poll.WaitPlayingError code={Code}: {Error}",
								code,
								ex.Message
							);
						}
					}
					else
					{
						stall++;
						Telemetry.Debug(
							"Pristine.Poll.Stall code={Code} {Stall}/{Max}",
							code,
							stall,
							MaxStall
						);
						var hasReady = false;
						try
						{
							hasReady = await page.EvaluateAsync<bool>(
									"() => {var els=document.querySelectorAll('body > audio');for(var i=0;i<els.length;i++){var el=els[i];if(el.paused&&el.readyState>=4&&el.hasAttribute('src'))return true;}return false;}"
								)
								.WaitAsync(ct);
						}
						catch (OperationCanceledException)
						{
							Telemetry.Warn("Pristine.Poll.ReadyCheckCancelled code={Code}", code);
							throw;
						}
						catch (Exception ex)
						{
							Telemetry.Debug(
								"Pristine.Poll.ReadyCheckFailed code={Code}: {Error}",
								code,
								ex.Message
							);
						}

						if (hasReady)
						{
							Telemetry.Debug(
								"Pristine.Poll.ReadyPausedRetryingPlay code={Code}",
								code
							);
							try
							{
								await page.GetByRole(
										AriaRole.Button,
										new PageGetByRoleOptions
										{
											NameString = "Play",
											Exact = true,
										}
									)
									.ClickAsync(new LocatorClickOptions { Timeout = 3000 })
									.WaitAsync(ct);
							}
							catch (Exception ex) when (ex is not OperationCanceledException)
							{
								Telemetry.Debug(
									"Pristine.Poll.RetryPlayFailed code={Code}: {Error}",
									code,
									ex.Message
								);
							}
						}
						else if (stall == 5)
						{
							Telemetry.Debug("Pristine.Poll.Stall5RetryPlaynow code={Code}", code);
							try
							{
								await page.Locator(
										"li[data-album-track-id] button.track-title-button"
									)
									.First.ClickAsync(new LocatorClickOptions { Timeout = 3000 })
									.WaitAsync(ct);
							}
							catch (Exception ex) when (ex is not OperationCanceledException)
							{
								Telemetry.Debug(
									"Pristine.Poll.StallRetryFailed code={Code}: {Error}",
									code,
									ex.Message
								);
							}
						}

						await Task.Delay(PollMs, ct);
					}
				}
			}
			finally
			{
				if (pendingDownloads.Count > 0)
				{
					Telemetry.Debug(
						"Pristine.Poll.AwaitPending code={Code} pending={Count}",
						code,
						pendingDownloads.Count
					);
					try
					{
						await Task.WhenAll(pendingDownloads).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						Telemetry.Warn(
							"Pristine.Poll.PendingDrainFailed code={Code}: {Error}",
							code,
							ex.Message
						);
					}
				}
			}

			if (trackNum > 0)
			{
				Telemetry.Debug(
					"Pristine.Poll.VerifySummary code={Code} attempted={Attempted} verified24={Verified}",
					code,
					trackNum,
					results.Count
				);
			}

			if (stall >= MaxStall)
			{
				Telemetry.Warn(
					"Pristine.Poll.StallLimitReached code={Code} stall={Stall}",
					code,
					stall
				);
			}

			await Task.Delay(2000, ct);
			Telemetry.Debug(
				"Pristine.Poll.Done code={Code} downloaded={Downloaded} expected={Expected} out={Out}",
				code,
				results.Count,
				expectedCount,
				albumOut
			);
			return new PristineAlbumResult
			{
				Code = code,
				Title = albumTitle,
				OutPath = albumOut,
				Expected = expectedCount,
				Downloaded = results.Count,
			};
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

	private static async Task<bool> WaitForLoginAsync(
		IPage page,
		CancellationToken ct,
		int timeoutS = 180
	)
	{
		try
		{
			if (
				page.Url.Contains("browse", StringComparison.OrdinalIgnoreCase)
				&& page.Url.Contains("login", StringComparison.OrdinalIgnoreCase) is false
			)
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
			await page.WaitForURLAsync(
					"**pristinestreaming.com/app/browse**",
					new PageWaitForURLOptions { Timeout = timeoutS * 1000 }
				)
				.WaitAsync(ct);
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

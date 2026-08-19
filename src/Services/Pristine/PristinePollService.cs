using System.Globalization;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristinePollService(PristineAlbumService albumService, PristineDownloader downloader)
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
		IPage page = await ctx.NewPageAsync();
		try
		{
			await page.GotoAsync("https://pristinestreaming.com/app/browse", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
			if (!await WaitForLoginAsync(page))
			{
				return new PristineAlbumResult { Code = code, Title = "unknown", OutPath = outDir, Expected = 0, Downloaded = 0 };
			}

			var albumId = await albumService.ResolveAlbumIdAsync(page, code, ct);
			if (albumId == null)
			{
				return new PristineAlbumResult { Code = code, Title = "unknown", OutPath = outDir, Expected = 0, Downloaded = 0 };
			}

			await page.GotoAsync($"https://pristinestreaming.com/app/browse/albums/{albumId}", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
			try
			{
				await page.WaitForSelectorAsync(".pp-album-view__title", new PageWaitForSelectorOptions { Timeout = 30000 });
			}
			catch
			{
			}

			var rawTitle = "";
			try
			{
				rawTitle = await page.EvaluateAsync<string>("() => document.querySelector('.pp-album-view__title')?.textContent?.trim()||'Unknown Album'") ?? "Unknown Album";
			}
			catch
			{
				rawTitle = "Unknown Album";
			}

			var albumTitle = PristineText.SanitizePathComponent(rawTitle);
			var albumOut = Path.Combine(outDir, albumTitle);
			Directory.CreateDirectory(albumOut);

			List<string> expectedTracks = await albumService.ParseTracklistAsync(page);
			var expectedCount = expectedTracks.Count;
			await albumService.DownloadArtworkAndPdfAsync(page, albumOut, albumTitle, http, ct);

			List<string> capturedUrls = [];
			page.Request += (_, req) =>
			{
				var url = req.Url;
				if (PristineText.IsAudioUrl(url) && !capturedUrls.Contains(url))
				{
					capturedUrls.Add(url);
				}
			};

			await albumService.StartPlaybackAsync(page, ct);

			HashSet<string> seenUrls = [];
			HashSet<string> seenTitles = [];
			var stall = 0;
			var trackNum = 0;

			while (stall < MaxStall)
			{
				ct.ThrowIfCancellationRequested();
				string? src = null;
				foreach (var c in capturedUrls)
				{
					if (!seenUrls.Contains(c))
					{
						src = c;
						break;
					}
				}

				if (src == null)
				{
					try
					{
						src = await page.EvaluateAsync<string?>("() => {var els=document.querySelectorAll('body > audio');for(var i=0;i<els.length;i++){var el=els[i];if(!el.paused&&el.hasAttribute('src'))return el.getAttribute('src');}return null;}");
					}
					catch
					{
					}
				}

				if (src != null && !seenUrls.Contains(src))
				{
					seenUrls.Add(src);
					stall = 0;
					trackNum++;
					var rawTrack = "";
					try
					{
						rawTrack = await page.EvaluateAsync<string>("() => document.querySelector('.pp-playbar__now-playing__track')?.textContent?.trim()||''") ?? "";
					}
					catch
					{
					}

					if (string.IsNullOrWhiteSpace(rawTrack))
					{
						rawTrack = $"Track {trackNum:00}";
					}

					if (seenTitles.Contains(rawTrack))
					{
						break;
					}

					seenTitles.Add(rawTrack);
					var normalized = PristineText.NormalizeTrackTitle(rawTrack);
					var safe = PristineText.SanitizePathComponent(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized));
					var ext = src.Contains(".flac", StringComparison.OrdinalIgnoreCase) ? ".flac" : ".mp3";
					var dest = Path.Combine(albumOut, $"{trackNum:00}. {safe}{ext}");

					try
					{
						await page.EvaluateAsync("() => document.querySelectorAll('body > audio').forEach(e=>e.pause())");
					}
					catch
					{
					}

					await downloader.DownloadAsync(src, dest, http, ct);
					if (expectedCount > 0 && trackNum >= expectedCount)
					{
						break;
					}

					await Task.Delay(PostDlWaitMs, ct);
					try
					{
						await page.EvaluateAsync("var f=document.querySelector('.pp-play-controls__main__primary > li:nth-child(3) > button');if(f)f.click();");
					}
					catch
					{
					}

					try
					{
						await page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('body > audio')).some(a => a.hasAttribute('src') && a.readyState >= 2)", null, new PageWaitForFunctionOptions { Timeout = 4000 });
					}
					catch
					{
					}

					try
					{
						await page.EvaluateAsync("var p=document.querySelector('.pp-play-controls__main__primary > li:nth-child(2) > button');if(p)p.click();");
					}
					catch
					{
					}

					try
					{
						await page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('body > audio')).some(a => !a.paused)", null, new PageWaitForFunctionOptions { Timeout = 3000 });
					}
					catch
					{
					}
				}
				else
				{
					stall++;
					var hasReady = false;
					try
					{
						hasReady = await page.EvaluateAsync<bool>("() => {var els=document.querySelectorAll('body > audio');for(var i=0;i<els.length;i++){var el=els[i];if(el.paused&&el.readyState>=4&&el.hasAttribute('src'))return true;}return false;}");
					}
					catch
					{
					}

					if (hasReady)
					{
						try
						{
							await page.EvaluateAsync("var p=document.querySelector('.pp-play-controls__main__primary > li:nth-child(2) > button');if(p)p.click();");
						}
						catch
						{
						}
					}
					else if (stall == 5)
					{
						try
						{
							await page.EvaluateAsync("var btn=document.querySelector('.pp-tracklist__item .pp-tracklist__item__playnow');if(btn){btn.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true}));}");
						}
						catch
						{
						}
					}

					await Task.Delay(PollMs, ct);
				}
			}

			await Task.Delay(10000, ct);
			return new PristineAlbumResult { Code = code, Title = albumTitle, OutPath = albumOut, Expected = expectedCount, Downloaded = seenTitles.Count };
		}
		finally
		{
			try
			{
				await page.CloseAsync();
			}
			catch
			{
			}
		}
	}

	private static async Task<bool> WaitForLoginAsync(IPage page, int timeoutS = 180)
	{
		try
		{
			if (page.Url.Contains("browse", StringComparison.OrdinalIgnoreCase) && !page.Url.Contains("login", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		catch
		{
		}

		try
		{
			await page.WaitForURLAsync("**pristinestreaming.com/app/browse**", new PageWaitForURLOptions { Timeout = timeoutS * 1000 });
			return true;
		}
		catch
		{
			return false;
		}
	}
}

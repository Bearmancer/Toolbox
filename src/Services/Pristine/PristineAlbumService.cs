using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristineAlbumService(PristineDownloader downloader)
{
	private static readonly string PristineApp = "https://pristinestreaming.com/app/browse";
	private static readonly string S3Covers = "https://s3-eu-west-1.amazonaws.com/pristine-classical-storage/covers/";

	public async Task<int?> ResolveAlbumIdAsync(IPage page, string code, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		const string searchSelector = ".pp-navbar__search__input";
		for (var attempt = 0; attempt < 3; attempt++)
		{
			try
			{
				await page.ClickAsync(searchSelector, new PageClickOptions { Timeout = 3000 });
			}
			catch
			{
			}

			try
			{
				await page.EvaluateAsync(@"var el=document.querySelector('.pp-navbar__search__input');if(el){el.value='';el.dispatchEvent(new Event('input',{bubbles:true}));}");
			}
			catch
			{
			}

			try
			{
				await page.FillAsync(searchSelector, code);
			}
			catch
			{
				try
				{
					await page.EvaluateAsync($"var el=document.querySelector('.pp-navbar__search__input');if(el){{el.value='{code}';el.dispatchEvent(new Event('input',{{bubbles:true}}));el.dispatchEvent(new Event('change',{{bubbles:true}}));}}");
				}
				catch
				{
				}
			}

			try
			{
				await page.EvaluateAsync("var el=document.querySelector('.pp-navbar__search__input');if(el){el.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',keyCode:13,bubbles:true}));el.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',keyCode:13,bubbles:true}));}");
			}
			catch
			{
			}

			try
			{
				await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5000 });
			}
			catch
			{
			}

			var searchUrl = "";
			try
			{
				searchUrl = page.Url;
			}
			catch
			{
			}

			var shortCode = code.Length > 4 ? code[4..] : code;
			if (!searchUrl.Contains(code, StringComparison.OrdinalIgnoreCase) && !searchUrl.Contains(shortCode, StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
				}
				catch
				{
				}

				continue;
			}

			string[] sels = ["[href*='/albums/']", ".pp-browse-grid__item", ".pp-search-results__item"];
			var clicked = false;
			foreach (var sel in sels)
			{
				IElementHandle? el = null;
				try
				{
					el = await page.QuerySelectorAsync(sel);
				}
				catch
				{
					continue;
				}

				if (el == null)
				{
					continue;
				}

				try
				{
					await page.ClickAsync(sel, new PageClickOptions { Timeout = 5000 });
				}
				catch
				{
					try
					{
						await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
					}
					catch
					{
					}

					clicked = true;
					break;
				}

				try
				{
					await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10000 });
				}
				catch
				{
				}

				var currentUrl = "";
				try
				{
					currentUrl = page.Url;
				}
				catch
				{
				}

				if (currentUrl.Contains("/albums/", StringComparison.OrdinalIgnoreCase))
				{
					var last = currentUrl.TrimEnd('/').Split('/')[^1];
					if (!int.TryParse(last, out var id))
					{
						clicked = true;
						break;
					}

					var title = "";
					try
					{
						title = await page.EvaluateAsync<string>("() => document.querySelector('.pp-album-view__title')?.textContent?.trim()||''") ?? "";
					}
					catch
					{
					}

					if (title.Contains(code, StringComparison.OrdinalIgnoreCase))
					{
						return id;
					}

					try
					{
						await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
					}
					catch
					{
					}

					clicked = true;
					break;
				}

				clicked = true;
				break;
			}

			if (!clicked)
			{
				try
				{
					await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
				}
				catch
				{
				}
			}
		}

		return null;
	}

	public async Task StartPlaybackAsync(IPage page, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		try
		{
			await page.EvaluateAsync("var t=document.querySelector('.pp-seekbar--togglebutton');if(t&&t.value!=='1')t.click();");
		}
		catch
		{
		}

		try
		{
			await page.WaitForSelectorAsync(".pp-tracklist__item", new PageWaitForSelectorOptions { Timeout = 15000 });
		}
		catch
		{
		}

		var clicked = false;
		try
		{
			await page.HoverAsync(".pp-tracklist__item");
			await page.ClickAsync(".pp-tracklist__item .pp-tracklist__item__playnow", new PageClickOptions { Timeout = 5000 });
			clicked = true;
		}
		catch
		{
		}

		if (!clicked)
		{
			try
			{
				await page.EvaluateAsync("var btn=document.querySelector('.pp-tracklist__item .pp-tracklist__item__playnow');if(btn){btn.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true}));} else{var item=document.querySelector('.pp-tracklist__item');if(item)item.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,cancelable:true}));}");
			}
			catch
			{
			}
		}

		try
		{
			await page.WaitForFunctionAsync("() => !!document.querySelector('body > audio[src]')", null, new PageWaitForFunctionOptions { Timeout = 5000 });
		}
		catch
		{
		}
	}

	public async Task<List<string>> ParseTracklistAsync(IPage page)
	{
		try
		{
			var raw = await page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.pp-tracklist__item__title')).map(el=>el.textContent.trim())");
			return raw != null ? [.. raw] : [];
		}
		catch
		{
			return [];
		}
	}

	public async Task DownloadArtworkAndPdfAsync(IPage page, string albumOut, string albumTitle, HttpClient http, CancellationToken ct = default)
	{
		var artworkSrc = "";
		try
		{
			artworkSrc = await page.EvaluateAsync<string>("() => document.querySelector('.pp-album-view__artwork > img')?.src || ''") ?? "";
		}
		catch
		{
		}

		if (string.IsNullOrEmpty(artworkSrc))
		{
			return;
		}

		var imgFile = artworkSrc.Split('/')[^1].Split('?')[0];
		var ext = Path.GetExtension(imgFile);
		var nameNoExt = Path.GetFileNameWithoutExtension(imgFile);
		var imgDest = Path.Combine(albumOut, $"{albumTitle}{ext}");
		await downloader.DownloadAsync(artworkSrc, imgDest, http, ct);
		var pdfUrl = $"{S3Covers}{nameNoExt}.pdf";
		var pdfDest = Path.Combine(albumOut, $"{nameNoExt}.pdf");
		var ok = await downloader.DownloadAsync(pdfUrl, pdfDest, http, ct);
		if (!ok)
		{
			try
			{
				if (File.Exists(pdfDest))
				{
					File.Delete(pdfDest);
				}
			}
			catch
			{
			}
		}
	}
}

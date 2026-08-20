using System.Text.Json;
using Core;
using ErrorOr;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristineAlbumService(PristineDownloader downloader)
{
	private static readonly string PristineApp = "https://pristinestreaming.com/app/browse";
	private static readonly string S3Covers = "https://s3-eu-west-1.amazonaws.com/pristine-classical-storage/covers/";

	public async Task<ErrorOr<int?>> ResolveAlbumIdAsync(IPage page, string code, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		Telemetry.Info("Pristine.Album.ResolveStart code={Code}", code);
		
		const string searchSelector = ".pp-navbar__search__input";
		using CancellationTokenSource resolveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		resolveCts.CancelAfter(TimeSpan.FromSeconds(45));
		CancellationToken resolveCt = resolveCts.Token;

		string jsClear(string sel) => "var el=document.querySelector(" + JsonSerializer.Serialize(sel) + ");if(el){el.value='';el.dispatchEvent(new Event('input',{bubbles:true}));}";
		string jsFill(string sel, string val) => "var el=document.querySelector(" + JsonSerializer.Serialize(sel) + ");if(el){el.value=" + JsonSerializer.Serialize(val) + ";el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));}";
		string jsEnter(string sel) => "var el=document.querySelector(" + JsonSerializer.Serialize(sel) + ");if(el){el.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',keyCode:13,bubbles:true}));el.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',keyCode:13,bubbles:true}));}";
		
		for (var attempt = 0; attempt < 3; attempt++)
		{
			resolveCt.ThrowIfCancellationRequested();
			Telemetry.Debug("Pristine.Album.Attempt code={Code} attempt={Attempt}/3", code, attempt + 1);
			
			try
			{
			ILocator search = page.Locator(searchSelector);

			bool searchAttached;
			try
			{
				await search.WaitForAsync(new LocatorWaitForOptions
				{
					State = WaitForSelectorState.Attached,
					Timeout = 5000
				}).WaitAsync(resolveCt);
				searchAttached = true;
			}
			catch (Exception ex)
			{
				searchAttached = false;
				Telemetry.Warn("Pristine.Album.SearchMissing code={Code} attempt={Attempt}: {Error}", code, attempt + 1, ex.Message);
				(string Url, string Title, string Snippet) diag = await DumpPageAsync(page, resolveCt);
				Telemetry.Warn("Pristine.Album.PageDiag code={Code} url={Url} title={Title} snippet={Snippet}", code, diag.Url, diag.Title, diag.Snippet);
			}

			if (searchAttached)
			{
				try
				{
					await search.ClickAsync(new LocatorClickOptions { Timeout = 5000 }).WaitAsync(resolveCt);
				}
				catch (Exception ex)
				{
					Telemetry.Debug("Pristine.Album.ClickIgnored code={Code}: {Error}", code, ex.Message);
				}

				await page.EvaluateAsync(jsClear(searchSelector)).WaitAsync(resolveCt);
				try
				{
					await search.FillAsync(code, new LocatorFillOptions { Timeout = 5000 }).WaitAsync(resolveCt);
				}
				catch (Exception ex)
				{
					Telemetry.Debug("Pristine.Album.FillFallback code={Code}: {Error}", code, ex.Message);
					await page.EvaluateAsync(jsFill(searchSelector, code)).WaitAsync(resolveCt);
				}
				Telemetry.Debug("Pristine.Album.Filled code={Code} attempt={Attempt}", code, attempt + 1);

				await page.EvaluateAsync(jsEnter(searchSelector)).WaitAsync(resolveCt);
				Telemetry.Debug("Pristine.Album.Enter code={Code} attempt={Attempt}", code, attempt + 1);
			}

			try
			{
				await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5000 }).WaitAsync(resolveCt);
			}
			catch (Exception)
			{
			}
				
				var url = page.Url;
				Telemetry.Debug("Pristine.Album.SearchUrl code={Code} url={Url}", code, url);
				
				var shortCode = code.Length > 4 ? code[4..] : code;
				var urlMatches = url.Contains(code, StringComparison.OrdinalIgnoreCase) || 
				                 url.Contains(shortCode, StringComparison.OrdinalIgnoreCase);
				
				if (urlMatches is false)
				{
					Telemetry.Debug("Pristine.Album.UrlMismatch code={Code} url={Url} short={Short}", code, url, shortCode);
					await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(resolveCt);
					continue;
				}
				
				var sels = new[] { "[href*='/albums/']", ".pp-browse-grid__item", ".pp-search-results__item" };
				var clicked = false;
				
				foreach (var sel in sels)
				{
					ILocator locator = page.Locator(sel).First;
					
					try
					{
					await locator.WaitForAsync(new LocatorWaitForOptions
					{
						State = WaitForSelectorState.Attached,
						Timeout = 3000
					}).WaitAsync(resolveCt);
						
						var href = await locator.GetAttributeAsync("href").WaitAsync(resolveCt) ?? string.Empty;
						Telemetry.Debug("Pristine.Album.ElementFound sel={Sel} code={Code} attempt={Attempt} href={Href}", sel, code, attempt + 1, href.Length > 80 ? href[..80] : href);
						
						await locator.ClickAsync(new LocatorClickOptions { Timeout = 5000 }).WaitAsync(resolveCt);
						Telemetry.Debug("Pristine.Album.ClickSelOk sel={Sel} code={Code}", sel, code);
						
						await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10000 }).WaitAsync(resolveCt);
						Telemetry.Debug("Pristine.Album.DOMContentOk sel={Sel} code={Code}", sel, code);
						
						var currentUrl = page.Url;
						Telemetry.Debug("Pristine.Album.CurrentUrl sel={Sel} code={Code} url={Url}", sel, code, currentUrl);
						
						if (currentUrl.Contains("/albums/", StringComparison.OrdinalIgnoreCase))
						{
							var last = currentUrl.TrimEnd('/').Split('/')[^1];
							if (int.TryParse(last, out var id) is false)
							{
								Telemetry.Warn("Pristine.Album.IdParseFailed url={Url} token={Token} code={Code}", currentUrl, last, code);
								clicked = true;
								break;
							}
							
							var title = await page.Locator(".pp-album-view__title").TextContentAsync(new LocatorTextContentOptions { Timeout = 5000 }).WaitAsync(resolveCt) ?? string.Empty;
							Telemetry.Debug("Pristine.Album.TitleRead code={Code} title={Title}", code, title);
							
							var titleMatches = title.Contains(code, StringComparison.OrdinalIgnoreCase);
							var urlMatchesId = currentUrl.Contains(code, StringComparison.OrdinalIgnoreCase);
							
							if (titleMatches || urlMatchesId)
							{
								Telemetry.Info("Pristine.Album.Resolved code={Code} id={Id} title={Title}", code, id, title);
								return id;
							}
							
							Telemetry.Debug("Pristine.Album.TitleMismatch code={Code} title={Title} url={Url}", code, title, currentUrl);
							await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(resolveCt);
							clicked = true;
							break;
						}
						
						clicked = true;
						break;
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex)
					{
						Telemetry.Warn("Pristine.Album.SelectorFailed sel={Sel} code={Code} attempt={Attempt}: {Error}", sel, code, attempt + 1, ex.Message);
						continue;
					}
				}
				
				if (clicked is false)
				{
					Telemetry.Debug("Pristine.Album.NoClick code={Code} attempt={Attempt}", code, attempt + 1);
					await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(resolveCt);
				}
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn("Pristine.Album.ResolveCancelled code={Code} attempt={Attempt}", code, attempt + 1);
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Warn("Pristine.Album.ResolveFailed code={Code} attempt={Attempt}: {Error}", code, attempt + 1, ex.Message);
				try
				{
					await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(resolveCt);
				}
				catch
				{
				}
			}
		}
		
		Telemetry.Warn("Pristine.Album.ResolveFailed code={Code} attempts=3", code);
		return Errors.Pristine.ResolveFailed(code);
	}

	private static async Task<(string Url, string Title, string Snippet)> DumpPageAsync(IPage page, CancellationToken ct)
	{
		try
		{
			var url = page.Url;
			var title = await page.TitleAsync().WaitAsync(ct);
			var snippet = await page.EvaluateAsync<string>("() => (document.body ? (document.body.innerText || '') : '').slice(0,500)").WaitAsync(ct) ?? string.Empty;
			return (url, title, snippet.Replace("\n", " ").Replace("\r", " "));
		}
		catch (Exception ex)
		{
			return (page.Url, string.Empty, "dump-failed: " + ex.Message);
		}
	}

	public async Task StartPlaybackAsync(IPage page, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		Telemetry.Debug("Pristine.Album.StartPlayback");
		try
		{
			await page.EvaluateAsync("var t=document.querySelector('.pp-seekbar--togglebutton');if(t&&t.value!=='1')t.click();").WaitAsync(ct);
			Telemetry.Debug("Pristine.Album.ToggleSeekbarOk");
		}
		catch (OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Album.ToggleSeekbarCancelled");
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Album.ToggleSeekbarFailed: {Error}", ex.Message);
		}

		try
		{
			await page.WaitForSelectorAsync(".pp-tracklist__item", new PageWaitForSelectorOptions { Timeout = 15000 }).WaitAsync(ct);
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

		var clicked = false;
		try
		{
			await page.HoverAsync(".pp-tracklist__item").WaitAsync(ct);
			await page.ClickAsync(".pp-tracklist__item .pp-tracklist__item__playnow", new PageClickOptions { Timeout = 5000 }).WaitAsync(ct);
			clicked = true;
			Telemetry.Debug("Pristine.Album.PlaynowClickOk");
		}
		catch (OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Album.PlaynowClickCancelled");
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Album.PlaynowClickFailed: {Error}", ex.Message);
		}

		if (clicked is false)
		{
			try
			{
				await page.EvaluateAsync("var btn=document.querySelector('.pp-tracklist__item .pp-tracklist__item__playnow');if(btn){btn.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true}));} else{var item=document.querySelector('.pp-tracklist__item');if(item)item.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,cancelable:true}));}").WaitAsync(ct);
				Telemetry.Debug("Pristine.Album.PlaynowFallbackOk");
			}
			catch (OperationCanceledException)
			{
				Telemetry.Warn("Pristine.Album.PlaynowFallbackCancelled");
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Debug("Pristine.Album.PlaynowFallbackFailed: {Error}", ex.Message);
			}
		}

		try
		{
			await page.WaitForFunctionAsync("() => !!document.querySelector('body > audio[src]')", null, new PageWaitForFunctionOptions { Timeout = 5000 }).WaitAsync(ct);
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

	public async Task<ErrorOr<List<string>>> ParseTracklistAsync(IPage page, CancellationToken ct = default)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		try
		{
			var raw = await page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.pp-tracklist__item__title')).map(el=>el.textContent.trim())").WaitAsync(ct);
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

	public async Task DownloadArtworkAndPdfAsync(IPage page, string albumOut, string albumTitle, HttpClient http, CancellationToken ct = default)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		var artworkSrc = string.Empty;
		try
		{
			artworkSrc = await page.EvaluateAsync<string>("() => document.querySelector('.pp-album-view__artwork > img')?.src || ''").WaitAsync(ct) ?? string.Empty;
			Telemetry.Debug("Pristine.Album.ArtworkSrc src={Src}", artworkSrc.Length > 120 ? artworkSrc[..120] : artworkSrc);
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
		var nameNoExt = Path.GetFileNameWithoutExtension(imgFile);
		var imgDest = Path.Combine(albumOut, $"{albumTitle}{ext}");
		Telemetry.Debug("Pristine.Album.ArtworkDownload src={Src} dest={Dest}", artworkSrc.Length > 80 ? artworkSrc[..80] : artworkSrc, Path.GetFileName(imgDest));
		var imgOk = await downloader.DownloadAsync(artworkSrc, imgDest, http, ct);
		Telemetry.Debug("Pristine.Album.ArtworkResult dest={Dest} ok={Ok}", Path.GetFileName(imgDest), imgOk);
		var pdfUrl = $"{S3Covers}{nameNoExt}.pdf";
		var pdfDest = Path.Combine(albumOut, $"{nameNoExt}.pdf");
		Telemetry.Debug("Pristine.Album.PdfDownload url={Url} dest={Dest}", pdfUrl[..Math.Min(80, pdfUrl.Length)], Path.GetFileName(pdfDest));
		var ok = await downloader.DownloadAsync(pdfUrl, pdfDest, http, ct);
		if (ok is false)
		{
			Telemetry.Debug("Pristine.Album.PdfNotFound url={Url}", pdfUrl[..Math.Min(80, pdfUrl.Length)]);
			try
			{
				if (File.Exists(pdfDest))
					File.Delete(pdfDest);
			}
			catch (Exception ex)
			{
				Telemetry.Debug("Pristine.Album.PdfDeleteFailed dest={Dest}: {Error}", Path.GetFileName(pdfDest), ex.Message);
			}
		}
		else
		{
			Telemetry.Debug("Pristine.Album.PdfOk dest={Dest}", Path.GetFileName(pdfDest));
		}
	}
}

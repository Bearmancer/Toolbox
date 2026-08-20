using System.Text.Json;
using Core;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristineBrowser
{
	public async Task<IBrowserContext> CreateAsync(bool headless, CancellationToken ct = default)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		Telemetry.Debug("Pristine.Browser.Create headless={Headless} userData={Dir}", headless, PristinePaths.UserDataDir);
		IBrowserContext ctx;
		try
		{
			IPlaywright pw = await Playwright.CreateAsync().WaitAsync(ct);
			ctx = await pw.Chromium.LaunchPersistentContextAsync(
				PristinePaths.UserDataDir,
				new BrowserTypeLaunchPersistentContextOptions
				{
					Channel = "msedge",
					Headless = headless,
					AcceptDownloads = true,
					Args = ["--autoplay-policy=no-user-gesture-required"],
				}
			).WaitAsync(ct);
			Telemetry.Debug("Pristine.Browser.Launched headless={Headless} dir={Dir}", headless, PristinePaths.UserDataDir);
		}
		catch (OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Browser.LaunchCancelled");
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Error("Pristine.Browser.LaunchFailed: {Error}", ex.Message);
			throw;
		}

		var authPath = PristinePaths.AuthPath;
		if (File.Exists(authPath))
		{
			try
			{
				var json = await File.ReadAllTextAsync(authPath, ct);
				using JsonDocument doc = JsonDocument.Parse(json);
				if (doc.RootElement.TryGetProperty("cookies", out JsonElement cookiesEl))
				{
					List<Cookie> cookies = [];
					foreach (JsonElement c in cookiesEl.EnumerateArray())
					{
						if (c.TryGetProperty("name", out JsonElement nameEl) && nameEl.GetString() is string nameValue && !string.IsNullOrEmpty(nameValue) && c.TryGetProperty("value", out JsonElement valueEl) && valueEl.GetString() is string cookieValue)
						{
							var isHostCookie = nameValue.StartsWith("__Host-", StringComparison.Ordinal);
							
							Cookie cookie = new()
							{
								Name = nameValue,
								Value = cookieValue,
								Domain = isHostCookie ? null : (c.TryGetProperty("domain", out JsonElement dEl) && dEl.GetString() is string domainValue ? domainValue : null),
								Path = isHostCookie ? "/" : (c.TryGetProperty("path", out JsonElement pEl) && pEl.GetString() is string pathValue ? pathValue : "/"),
							};
							
							if (c.TryGetProperty("expires", out JsonElement e) && e.ValueKind is not JsonValueKind.Null)
							{
								try
								{
									cookie.Expires = (float)e.GetDouble();
								}
								catch (Exception ex)
								{
									Telemetry.Debug("Pristine.Browser.ExpiresParseFailed name={Name}: {Error}", nameValue, ex.Message);
								}
							}

							if (c.TryGetProperty("httpOnly", out JsonElement hEl))
							{
								try
								{
									cookie.HttpOnly = hEl.GetBoolean();
								}
								catch (Exception ex)
								{
									Telemetry.Debug("Pristine.Browser.HttpOnlyParseFailed name={Name}: {Error}", nameValue, ex.Message);
								}
							}

							if (c.TryGetProperty("secure", out JsonElement sEl))
							{
								try
								{
									cookie.Secure = sEl.GetBoolean();
								}
								catch (Exception ex)
								{
									Telemetry.Debug("Pristine.Browser.SecureParseFailed name={Name}: {Error}", nameValue, ex.Message);
								}
							}
							
						if (isHostCookie)
						{
							cookie.Secure = true;
							cookie.Path = "/";
							cookie.Domain = null;
							cookie.Url = "https://pristinestreaming.com"; // ponytail: host-cookie origin for pristinestreaming.com
						}

							if (c.TryGetProperty("sameSite", out JsonElement ssEl) && ssEl.GetString() is string sameSiteValue)
							{
								cookie.SameSite = Enum.TryParse<SameSiteAttribute>(
									sameSiteValue,
									ignoreCase: true,
									out SameSiteAttribute parsed
								)
									? parsed
									: SameSiteAttribute.Lax;
							}

							Telemetry.Debug(
								"Pristine.Browser.CookieCandidate name={Name} host={Host} domain={Domain} path={Path} secure={Secure}",
								cookie.Name,
								isHostCookie,
								cookie.Domain ?? "(none)",
								cookie.Path ?? "/",
								cookie.Secure ?? false);

							cookies.Add(cookie);
						}
						else
						{
							Telemetry.Debug("Pristine.Browser.SkipCookieMissingNameOrValue");
						}
					}

					if (cookies.Count > 0)
					{
						IReadOnlyList<string> domains = [.. cookies.Select(c => c.Domain ?? "(host)").Distinct().OrderBy(d => d)];
						Telemetry.Info("Pristine.Browser.AddCookies count={Count} domains={Domains}", cookies.Count, string.Join(",", domains));
						List<Cookie> okCookies = [];
						List<string> badCookies = [];
						foreach (Cookie ck in cookies)
						{
							try
							{
await ctx.AddCookiesAsync([ck]).WaitAsync(ct);
							okCookies.Add(ck);
							}
							catch (OperationCanceledException)
							{
								throw;
							}
							catch (Exception ex)
							{
								var entry = $"{ck.Name}@{ck.Domain}:{ex.Message}";
								badCookies.Add(entry);
								Telemetry.Warn("Pristine.Browser.CookieRejected name={Name} domain={Domain} error={Error}", ck.Name, ck.Domain ?? "(host)", ex.Message);
							}
						}

						Telemetry.Info("Pristine.Browser.CookiesApplied ok={Ok} rejected={Rejected}", okCookies.Count, badCookies.Count);
						if (badCookies.Count > 0)
							Telemetry.Debug("Pristine.Browser.RejectedCookies {Rejected}", string.Join(" | ", badCookies));
					}
					else
					{
						Telemetry.Warn("Pristine.Browser.NoCookiesAfterParse path={Path}", authPath);
					}
				}
				else
				{
					Telemetry.Warn("Pristine.Browser.NoCookiesProperty path={Path}", authPath);
				}

				if (doc.RootElement.TryGetProperty("origins", out JsonElement originsEl))
				{
					foreach (JsonElement origin in originsEl.EnumerateArray())
					{
						var originUrl = origin.TryGetProperty("origin", out JsonElement oEl) && oEl.GetString() is string oVal ? oVal : string.Empty;
						if (string.IsNullOrEmpty(originUrl) || origin.TryGetProperty("localStorage", out JsonElement lsEl) is false)
						{
							continue;
						}

						List<string> lines = [];
						foreach (JsonElement item in lsEl.EnumerateArray())
						{
							var itemName = item.TryGetProperty("name", out JsonElement nEl) && nEl.GetString() is string nVal ? nVal : string.Empty;
							var itemValue = item.TryGetProperty("value", out JsonElement vEl) && vEl.GetString() is string vVal ? vVal : string.Empty;
							if (string.IsNullOrEmpty(itemName))
							{
								continue;
							}

							var n = JsonSerializer.Serialize(itemName);
							var v = JsonSerializer.Serialize(itemValue);
							lines.Add($"localStorage.setItem({n}, {v});");
						}

						if (lines.Count > 0)
						{
							var script = string.Join("\n", lines);
							Telemetry.Debug("Pristine.Browser.AddInitScript origin={Origin} items={Count}", originUrl, lines.Count);
							try
							{
								await ctx.AddInitScriptAsync(
									$"if (window.location.origin === {JsonSerializer.Serialize(originUrl)}) {{\n{script}\n}}"
								).WaitAsync(ct);
							}
							catch (OperationCanceledException)
							{
								throw;
							}
							catch (Exception ex)
							{
								Telemetry.Warn("Pristine.Browser.AddInitScriptFailed origin={Origin}: {Error}", originUrl, ex.Message);
							}
						}
					}
				}
			}
			catch (JsonException ex)
			{
				Telemetry.Warn("Pristine.Browser.AuthJsonInvalid path={Path}: {Error}", authPath, ex.Message);
			}
			catch (IOException ex)
			{
				Telemetry.Warn("Pristine.Browser.AuthReadFailed path={Path}: {Error}", authPath, ex.Message);
			}
			catch (Exception ex)
			{
				Telemetry.Warn("Pristine.Browser.AuthRestoreFailed path={Path}: {Error}", authPath, ex.Message);
			}
		}
		else
		{
			Telemetry.Debug("Pristine.Browser.NoAuthFile path={Path}", authPath);
		}

		return ctx;
	}
}

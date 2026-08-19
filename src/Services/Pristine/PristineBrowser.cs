using System.Text.Json;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristineBrowser
{
	public async Task<IBrowserContext> CreateAsync(bool headless, CancellationToken ct = default)
	{
		IPlaywright pw = await Playwright.CreateAsync();
		IBrowserContext ctx = await pw.Chromium.LaunchPersistentContextAsync(
			PristinePaths.UserDataDir,
			new BrowserTypeLaunchPersistentContextOptions
			{
				Channel = "msedge",
				Headless = headless,
				AcceptDownloads = true,
				Args = ["--autoplay-policy=no-user-gesture-required"],
			}
		);

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
						Cookie cookie = new()
						{
							Name = c.GetProperty("name").GetString() ?? "",
							Value = c.GetProperty("value").GetString() ?? "",
							Domain = c.TryGetProperty("domain", out JsonElement d)
								? d.GetString()
								: null,
							Path = c.TryGetProperty("path", out JsonElement p)
								? p.GetString() ?? "/"
								: "/",
						};
						if (c.TryGetProperty("expires", out JsonElement e) && e.ValueKind != JsonValueKind.Null)
						{
							cookie.Expires = (float)e.GetDouble();
						}

						if (c.TryGetProperty("httpOnly", out JsonElement h))
						{
							cookie.HttpOnly = h.GetBoolean();
						}

						if (c.TryGetProperty("secure", out JsonElement s))
						{
							cookie.Secure = s.GetBoolean();
						}

						if (c.TryGetProperty("sameSite", out JsonElement ss))
						{
							var v = ss.GetString();
							if (v != null)
							{
								cookie.SameSite = Enum.TryParse<SameSiteAttribute>(
									v,
									ignoreCase: true,
									out SameSiteAttribute parsed
								)
									? parsed
									: SameSiteAttribute.Lax;
							}
						}

						cookies.Add(cookie);
					}

					if (cookies.Count > 0)
					{
						await ctx.AddCookiesAsync(cookies);
					}
				}

				if (doc.RootElement.TryGetProperty("origins", out JsonElement originsEl))
				{
					foreach (JsonElement origin in originsEl.EnumerateArray())
					{
						var originUrl = origin.TryGetProperty("origin", out JsonElement o)
							? o.GetString()
							: null;
						if (
							originUrl == null
							|| !origin.TryGetProperty("localStorage", out JsonElement lsEl)
						)
						{
							continue;
						}

						List<string> lines = [];
						foreach (JsonElement item in lsEl.EnumerateArray())
						{
							var n = JsonSerializer.Serialize(item.GetProperty("name").GetString());
							var v = JsonSerializer.Serialize(item.GetProperty("value").GetString());
							lines.Add($"localStorage.setItem({n}, {v});");
						}

						if (lines.Count > 0)
						{
							var script = string.Join("\n", lines);
							await ctx.AddInitScriptAsync(
								$"if (window.location.origin === {JsonSerializer.Serialize(originUrl)}) {{\n{script}\n}}"
							);
						}
					}
				}
			}
			catch
			{
			}
		}

		return ctx;
	}
}

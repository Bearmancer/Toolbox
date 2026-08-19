# Phase 2: Browser Infrastructure

## Tasks

### Task 12: Create BrowserSetup.cs

**What to do:**
Create `src/Services/Reader/BrowserSetup.cs`:

```csharp
using ErrorOr;
using Patchright;

namespace Services.Reader;

public sealed class BrowserSetup
{
    private readonly Lazy<Task<IBrowser>> _browser;

    public BrowserSetup()
    {
        _browser = new Lazy<Task<IBrowser>>(CreateBrowserAsync);
    }

    public async Task<ErrorOr<BrowserPage>> CreatePageAsync(CancellationToken ct)
    {
        return await CreateContextAsync()
            .ThenAsync(async ctx =>
            {
                var page = await ctx.NewPageAsync();
                return new BrowserPage(page, ctx);
            });
    }

    private async Task<ErrorOr<IBrowserContext>> CreateContextAsync()
    {
        var browser = await _browser.Value;
        return await browser.NewContextAsync();
    }

    private async Task<IBrowser> CreateBrowserAsync()
    {
        return await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--disable-blink-features=AutomationControlled"]
        });
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/Services/Reader/Reader.csproj` succeeds

**QA:**

```bash
dotnet build src/Services/Reader/Reader.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add BrowserSetup with Patchright`

---

### Task 13: Create GhostNoise.cs

**What to do:**
Create `src/Services/Reader/GhostNoise.cs`:

```csharp
using Patchright;

namespace Services.Reader;

public sealed class GhostNoise
{
    public async Task ApplyAll(IPage page)
    {
        await page.AddInitScriptAsync("""
            Object.defineProperty(navigator, 'webdriver', { get: () => false });
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
            Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] });
            window.chrome = { runtime: {} };
            const getParameter = WebGLRenderingContext.prototype.getParameter;
            WebGLRenderingContext.prototype.getParameter = function(param) {
                if (param === 37445) return 'Intel Inc.';
                if (param === 37446) return 'Intel Iris OpenGL Engine';
                return getParameter.call(this, param);
            };
            const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
            HTMLCanvasElement.prototype.toDataURL = function(type) {
                if (type === 'image/png') {
                    const ctx = this.getContext('2d');
                    if (ctx) {
                        const imageData = ctx.getImageData(0, 0, this.width, this.height);
                        for (let i = 0; i < imageData.data.length; i += 4) {
                            imageData.data[i] ^= 1;
                        }
                        ctx.putImageData(imageData, 0, 0);
                    }
                }
                return originalToDataURL.apply(this, arguments);
            };
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            const originalCreateOscillator = AudioContext.prototype.createOscillator;
            AudioContext.prototype.createOscillator = function() {
                const oscillator = originalCreateOscillator.call(this);
                oscillator.frequency.setValueAtTime(440, this.currentTime);
                return oscillator;
            };
            """);
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/Services/Reader/Reader.csproj` succeeds

**QA:**

```bash
dotnet build src/Services/Reader/Reader.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add GhostNoise anti-detection`

---

### Task 14: Create CaptchaSolver.cs

**What to do:**
Create `src/Services/Reader/CaptchaSolver.cs`:

```csharp
using ErrorOr;
using Patchright;

namespace Services.Reader;

public sealed class CaptchaSolver
{
    private readonly HttpClient _http;

    public CaptchaSolver(HttpClient http) => _http = http;

    public async Task<ErrorOr<string>> SolveCaptchaAsync(IBrowserContext context, string siteKey, CancellationToken ct)
    {
        var captchaType = await DetectCaptchaType(context);
        return captchaType switch
        {
            CaptchaType.Nsl => await SolveNslAsync(context, siteKey, ct),
            CaptchaType.Capsolver => await SolveCapsolverAsync(siteKey, ct),
            _ => Errors.Reader.CaptchaSolvingFailed("Unknown CAPTCHA type")
        };
    }

    private static async Task<CaptchaType> DetectCaptchaType(IBrowserContext context)
    {
        var content = await context.Pages[0].ContentAsync();
        return content.Contains("nslovely") ? CaptchaType.Nsl : CaptchaType.Capsolver;
    }

    private static async Task<ErrorOr<string>> SolveNslAsync(IBrowserContext context, string siteKey, CancellationToken ct)
    {
        var page = context.Pages[0];
        var token = await page.EvaluateAsync<string>($"() => document.querySelector('[name=\"{siteKey}\"]').value");
        return token;
    }

    private async Task<ErrorOr<string>> SolveCapsolverAsync(string siteKey, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("https://api.capsolver.com/createTask", new { siteKey });
        return await response.Content.ReadAsStringAsync(ct);
    }
}

private enum CaptchaType { Nsl, Capsolver }
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments
- Separate CaptchaType enum into its own file (private to this file)

**Acceptance criteria:**

- `dotnet build src/Services/Reader/Reader.csproj` succeeds

**QA:**

```bash
dotnet build src/Services/Reader/Reader.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add CaptchaSolver with NSL + CapSolver`

---

### Task 15: Create AnubisPowSolver.cs

**What to do:**
Create `src/Services/Reader/AnubisPowSolver.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using ErrorOr;

namespace Services.Reader;

public sealed class AnubisPowSolver
{
    private const int MaxIterations = 10_000_000;

    public async Task<ErrorOr<string>> SolveAsync(string challenge, int leadingZeros, CancellationToken ct)
    {
        var prefix = new string('0', leadingZeros);
        return await Task.Run(() => FindNonce(challenge, prefix, ct), ct);
    }

    private static ErrorOr<string> FindNonce(string challenge, string prefix, CancellationToken ct)
    {
        for (var nonce = 0; nonce < MaxIterations; nonce++)
        {
            ct.ThrowIfCancellationRequested();
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{challenge}{nonce}")));
            if (hash.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return nonce.ToString();
        }
        return Errors.Reader.PowSolvingFailed;
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/Services/Reader/Reader.csproj` succeeds

**QA:**

```bash
dotnet build src/Services/Reader/Reader.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add AnubisPowSolver SHA256 proof-of-work`

---

## Verify Phase 2

```bash
dotnet build src/Services/Reader/Reader.csproj
```

Clean build. All browser infrastructure in place.

**Dependencies:** Phase 1
**Blocks:** Phase 3

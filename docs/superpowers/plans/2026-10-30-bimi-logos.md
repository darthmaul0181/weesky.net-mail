# BIMI logos in the sender avatars Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a sender's BIMI logo in the message list avatar, between the contact photo and Gravatar, for DMARC-passing messages only.

**Architecture:** The list FETCH reads two more headers and each row carries `bimiDomain`/`bimiSelector` when its topmost `Authentication-Results` says `dmarc=pass`. A new `GET /api/Avatars/Bimi` resolves the domain's BIMI TXT record, fetches the SVG through an SSRF-safe client, rasterises it to a PNG and caches it per domain+selector. The browser asks that route only for rows that carry `bimiDomain`.

**Tech Stack:** .NET 10 / xUnit / Moq (backend), React 18 / TanStack Query / Vitest (frontend), new NuGet `DnsClient` (TXT lookup) and `Svg.Skia` (+ the SkiaSharp native-assets package that runs on the Linux CI and the Docker image).

**Spec:** `docs/superpowers/specs/2026-10-30-bimi-logos-design.md` (extends `2026-10-30-sender-avatars-design.md`).

## Global Constraints

- Source order: contact photo, BIMI logo, Gravatar, initials. No new setting: `mail.showAvatars` gates everything; the route answers 404 before any lookup while it is not `"true"`.
- Logo fetch: `https` only, port 443, no redirect, no IP-literal host, address checked at connect time (loopback, private, link-local, multicast, unique-local, IPv4-mapped forms refused), one budget token over the whole fetch, body ≤ 32 KB, `Content-Type` `image/svg+xml`.
- The SVG is never forwarded: the route returns a server-made 80 px PNG with `nosniff` and `private, max-age=86400`.
- Cache per `domain|selector`, shared by users: hit 7 days, miss 24 h, failure 5 min, single-flight.
- Selector matches `[a-z0-9._-]{1,63}` and defaults to `default`; domain is a bare lower-cased DNS name, ≤ 253 characters.
- Repo rules: ≤ 5 parameters per method, no duplicated logic (extend, do not copy), structured `ILogger` messages, cancellation tokens everywhere, no comment on obvious code, commit messages two lines max, never starting or ending with `@`, ending with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Do not push. Revert `src/scotty.microservice/ApiDocumentation.xml` drift unrelated to the new route before committing. French strings keep U+00A0 where French wants it (the Edit tool writes a plain space: fix with PowerShell).
- Work on a new branch `bimi-logos` cut from `master`.

## Review Focus

1. A forged `dmarc=pass` in a lower `Authentication-Results` header (or a `dmarc=fail` on top) must never yield a logo.
2. A BIMI record whose `l=` is `http://`, an IP literal, a non-443 port or a redirect target must be a miss.
3. DNS rebinding and internal names: a host resolving to `127.0.0.1`, `10.x`, `169.254.169.254`, `::1`, `fc00::/7` or `::ffff:10.0.0.1` must never be connected to.
4. A hostile SVG (`<script>`, external `href`, `<image>`, DOCTYPE with entities) must be a miss or a harmless PNG, with nothing fetched.
5. A sender domain in odd shape (upper case, trailing dot, IDN, a `header.from` that is an address, missing property) must normalise or yield nothing, never throw or reach the DNS with garbage.

---

### Task 1: List rows carry the BIMI hint

**Files:**
- Modify: `src/scotty.microservice/Services/MailAuthenticationReader.cs`, `Services/ImapMessageCommands.cs:410` (header list), `Services/MailMessageMapper.cs:37`, `Models/Mail/MailMessageSummary.cs`
- Create: `src/scotty.microservice/Services/MailBimiHint.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Services/MailBimiHintTests.cs` (create), `.../Services/MailAuthenticationReaderTests.cs` (extend)

**Interfaces:**
- Produces: `MailAuthenticationReader.DmarcPassDomain(HeaderList headers) -> string?` (the lower-cased `header.from` of a `dmarc=pass` in the topmost header only); `internal static class MailBimiHint { public static readonly string[] Fields; public static (string? Domain, string? Selector) Parse(HeaderList headers); }`; `MailMessageSummary.BimiDomain` and `.BimiSelector` (`string?`, omitted from JSON when null like the other optional fields).

- [ ] **Step 1: Branch.** `git switch -c bimi-logos`.
- [ ] **Step 2: Write failing tests.** In `MailAuthenticationReaderTests`: `DmarcPassDomain_TopmostPass_ReturnsHeaderFromDomain`, `..._PassOnlyInLowerHeader_ReturnsNull`, `..._TopmostFail_ReturnsNull`, `..._NoHeaderFromProperty_ReturnsNull`, `..._UpperCaseAndTrailingDot_IsNormalised` (`header.from=PayPal.COM.` gives `paypal.com`), `..._HeaderFromIsAnAddress_ReturnsNull`. In `MailBimiHintTests`: `Parse_ReadsSelectorFromBimiSelectorHeader` (`v=BIMI1; s=news;` gives `news`), `Parse_BadSelector_FallsBackToNull` (selector null means "use default"), `Parse_NoDmarcPass_ReturnsNullDomainAndNullSelector`, `Fields_ContainsAuthenticationResultsAndBimiSelectorAndPriorityFields`.
- [ ] **Step 3: Run** `cd src && dotnet test --filter "MailBimiHintTests|MailAuthenticationReaderTests"`; expected FAIL (members missing).
- [ ] **Step 4: Implement** `DmarcPassDomain` inside `MailAuthenticationReader` by sharing the one `AuthenticationResults.TryParse` call and the property lookup with `Parse`; accept only a syntactically valid DNS name (reuse the validator written in Task 2 if it exists, otherwise a private `IsDomainName` that Task 2 then moves to `BimiRequest`). `MailBimiHint.Fields = [..MailPriorityReader.Fields, "Authentication-Results", "BIMI-Selector"]`. Replace `SummaryHeaders` in `ImapMessageCommands` with `MailBimiHint.Fields`; in `MailMessageMapper.FillSummary` set the two properties from `item.Headers`.
- [ ] **Step 5: Run** the same filter plus `dotnet test --filter "MailPriority|MailMessageMapper"`; expected PASS.
- [ ] **Step 6: Commit** `git commit -m "List rows carry the sender's DMARC-verified domain` / `for BIMI lookups"` (two lines plus the Co-Authored-By trailer).

### Task 2: Pure guards — request, record, address, SVG

**Files:**
- Create: `Models/BimiRequest.cs`, `Services/Bimi/BimiRecord.cs`, `Services/Bimi/PublicAddress.cs`, `Services/Bimi/SvgGuard.cs` (all under `src/scotty.microservice/`)
- Test: `scotty.microservice.Tests/Models/BimiRequestTests.cs`, `.../Services/Bimi/BimiRecordTests.cs`, `PublicAddressTests.cs`, `SvgGuardTests.cs` (create)

**Interfaces:**
- Produces: `sealed record BimiRequest([Required, StringLength(253)] string? Domain, [StringLength(63)] string? Selector)` with `bool IsValid`, `string DomainName` (lower-cased, trimmed) and `string SelectorName` (defaults to `default`); `static class BimiRecord { Uri? LogoOf(IEnumerable<string> txtRecords) }` (first record starting `v=BIMI1`, its `l=` value, only if `https`, port 443, host a DNS name, no user info); `static class PublicAddress { bool IsPublic(IPAddress address) }`; `static class SvgGuard { bool IsSafe(ReadOnlySpan<byte> svg) }`.

- [ ] **Step 1: Write failing tests** with the spec's values. `BimiRequestTests`: `paypal.com` valid; `PayPal.COM` normalised; `a@b.c`, `localhost`, `1.2.3.4`, `a..b`, 254-char name, selector `Bad Sel`, selector 64 chars invalid; trailing dot normalised. `BimiRecordTests`: `v=BIMI1; l=https://x.example/l.svg; a=…` gives the URI; `http://`, `https://x.example:8443/`, `https://10.0.0.1/l.svg`, `https://u:p@x.example/`, `l=` empty, no `v=BIMI1`, `l=` repeated all give null; whitespace around tags tolerated. `PublicAddressTests` (Theory): false for `127.0.0.1 10.1.2.3 172.16.0.1 192.168.1.1 169.254.169.254 100.64.0.1 0.0.0.0 224.0.0.1 ::1 fc00::1 fe80::1 ::ffff:10.0.0.1 ::ffff:127.0.0.1`, true for `93.184.216.34`, `2606:2800:220:1::1`. `SvgGuardTests`: plain `<svg>` with `<path>` safe; `<script>`, `<foreignObject>`, `<image href="http://…">`, `<use href="http://…">`, `<animate>`/`<set>`, an `href` not starting with `#`, `<style>` containing `@import` or `url(`, any `<!DOCTYPE>` / entity declaration, malformed XML, empty input all unsafe; `xlink:href="#grad"` safe.
- [ ] **Step 2: Run** `dotnet test --filter "BimiRequestTests|BimiRecordTests|PublicAddressTests|SvgGuardTests"`; expected FAIL.
- [ ] **Step 3: Implement** to the signatures above. `SvgGuard` reads with `XmlReader` (`DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities` 0) and rejects on the element/attribute rules in Step 1; any `XmlException` is unsafe. If Task 1 wrote a private `IsDomainName`, move it to `BimiRequest` and call it from `MailAuthenticationReader`.
- [ ] **Step 4: Run** the same filter; expected PASS.
- [ ] **Step 5: Commit** `Add BIMI request, record, address and SVG guards`.

### Task 3: One cache, two services

**Files:**
- Create: `Services/AnswerCache.cs`, `Services/IBimiLogoService.cs`, `Services/Bimi/BimiLogoService.cs`, `Services/Bimi/IDnsTxtResolver.cs`, `Services/Bimi/ISvgRasterizer.cs`
- Modify: `Services/GravatarService.cs` (use `AnswerCache`)
- Test: `.../Services/BimiLogoServiceTests.cs` (create); `GravatarServiceTests.cs` must stay green unchanged.

**Interfaces:**
- Produces: `sealed class AnswerCache(TimeProvider clock, long sizeBytes) : IDisposable` with `Task<AvatarImage?> GetAsync(string key, Func<Task<(AvatarImage? Image, TimeSpan Lifetime)>> fetch, CancellationToken cancellationToken)` — the in-memory size-limited cache, freshness judged on `clock`, single-flight per key, a waiter leaving never cancels the shared lookup (the exact behaviour now inside `GravatarService`). `IBimiLogoService.GetAsync(string domain, string selector, CancellationToken) -> Task<AvatarImage?>`. `IDnsTxtResolver.GetTxtAsync(string name, CancellationToken) -> Task<IReadOnlyList<string>>`. `ISvgRasterizer.ToPng(ReadOnlySpan<byte> svg, int size) -> byte[]?`.
- Consumes: Task 2 guards.

- [ ] **Step 1: Refactor.** Move the cache, `TryHeld`, `inFlight` and `LookUpAsync` bodies of `GravatarService` into `AnswerCache`; `GravatarService` keeps its hash, its fetch and its lifetimes. Run `dotnet test --filter GravatarServiceTests`; expected PASS before moving on.
- [ ] **Step 2: Write failing tests** for `BimiLogoService` with fakes for DNS, HTTP (`HttpMessageHandler` stub) and rasteriser: happy path returns `image/png`; queries `<selector>._bimi.<domain>`; falls back once to the parent domain when the first has no record (`mail.paypal.com` then `paypal.com`), never to a bare TLD; no record, bad `l=`, non-SVG content type, body over 32 KB, 3xx, 4xx/5xx, timeout, `SvgGuard` rejection and rasteriser null all return null; hit cached 7 days, miss 24 h, failure 5 min (fake `TimeProvider`); two concurrent calls make one fetch; the HTTP stub is never called when the record fails validation.
- [ ] **Step 3: Run** `dotnet test --filter BimiLogoServiceTests`; expected FAIL.
- [ ] **Step 4: Implement** `BimiLogoService` (internal sealed, primary constructor taking the resolver, `IHttpClientFactory`, rasteriser, `TimeProvider`, logger) with constants `ClientName = "bimi"`, `MaxBytes = 32 * 1024`, `FetchTimeout` 5 s, `RasterSize = 80`. Key `$"{domain}|{selector}"`. Read the body with the existing `ReadCappedAsync`.
- [ ] **Step 5: Run** `dotnet test --filter "BimiLogoServiceTests|GravatarServiceTests"`; expected PASS.
- [ ] **Step 6: Commit** `Share the avatar answer cache between Gravatar and BIMI`.

### Task 4: Real DNS, SSRF-safe HTTP, real rasteriser

**Files:**
- Modify: `scotty.microservice.core.csproj` (PackageReferences `DnsClient`, `Svg.Skia`, the SkiaSharp Linux native-assets package), `Configuration/ApplicationServicesConfiguration.cs:147` area
- Create: `Services/Bimi/DnsClientTxtResolver.cs`, `Services/Bimi/SvgSkiaRasterizer.cs`, `Services/Bimi/PublicOnlyConnect.cs`
- Test: `.../Services/Bimi/PublicOnlyConnectTests.cs`, `SvgSkiaRasterizerTests.cs` (create)

**Interfaces:**
- Produces: `static class PublicOnlyConnect { ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) }` — resolves the host, throws when no address `PublicAddress.IsPublic`, connects to the first public one (so the checked address is the one dialled); registration `AddHttpClient(BimiLogoService.ClientName, c => c.Timeout = 5 s).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = PublicOnlyConnect.ConnectAsync })`; singletons `IDnsTxtResolver`, `ISvgRasterizer`, `IBimiLogoService`.

- [ ] **Step 1: Write failing tests.** `PublicOnlyConnectTests`: a host resolving only to loopback/private (injectable resolver parameter or a `localhost` literal case) throws without opening a socket; mixed public/private picks the public. `SvgSkiaRasterizerTests`: a valid 100×100 SVG gives a PNG (magic bytes `89 50 4E 47`) of 80×80 (decode header); garbage gives null; an SVG with an external `<image href>` renders without any network call (use a handler counter or run with no network) or is refused by the guard first.
- [ ] **Step 2: Run** `dotnet test --filter "PublicOnlyConnectTests|SvgSkiaRasterizerTests"`; expected FAIL.
- [ ] **Step 3: Implement** the three classes and the DI lines. If `Svg.Skia` cannot render without loading external resources, or its native package does not run on both Windows and the Linux CI, stop and report: the fallback is a second library chosen with the owner, not a silent swap.
- [ ] **Step 4: Run** `cd src && dotnet build && dotnet test --filter "PublicOnlyConnectTests|SvgSkiaRasterizerTests"`; expected PASS. Check the Docker build still has the native asset (`docker/`), and record the result in the commit body if a file there changes.
- [ ] **Step 5: Commit** `Add DNS, pinned HTTPS and SVG rasteriser for BIMI`.

### Task 5: The `/api/Avatars/Bimi` route

**Files:**
- Modify: `Controllers/AvatarsController.cs`, `src/scotty.microservice/CLAUDE.md` (the `AvatarsController` bullet)
- Test: `.../Controllers/AvatarsControllerTests.cs` (extend)

**Interfaces:**
- Consumes: `IBimiLogoService`, `BimiRequest` (Task 2/3).
- Produces: `[HttpGet("Bimi")] Task<ActionResult> GetBimiLogo([FromQuery] BimiRequest request, CancellationToken)` with the same attributes, rate-limit policy and response codes as `GetAvatar`.

- [ ] **Step 1: Write failing tests:** avatars off gives 404 with the service never called; invalid domain or selector gives 400 with the service never called; service null gives 404; hit gives 200 `image/png`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, max-age=86400`; the service receives the lower-cased domain and the defaulted selector.
- [ ] **Step 2: Run** `dotnet test --filter AvatarsControllerTests`; expected FAIL.
- [ ] **Step 3: Implement.** Extract the shared "avatars are off for the caller" check and the image response into private helpers used by both actions (no copy of the preference lookup). Update the controller summary and `CLAUDE.md` bullet (BIMI route, DMARC-gated by the list, 404-before-lookup).
- [ ] **Step 4: Run** `cd src && dotnet test`; expected PASS. Restore unrelated `ApiDocumentation.xml` drift (`git diff --stat`), keep only the new route's lines.
- [ ] **Step 5: Commit** `Add the BIMI logo route`.

### Task 6: Frontend — the chain gains a BIMI step

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts:41` (`MailMessageSummary`: `bimiDomain?: string`, `bimiSelector?: string`), `src/api.ts:267` area, `modules/mail/list/avatar/useSenderAvatarUrl.ts`, `.../SenderAvatarView.tsx`, `modules/mail/list/MessageRow.tsx:162`
- Test: `useSenderAvatarUrl.test.tsx`, `SenderAvatarView.test.tsx`, `MessageRow.test.tsx` (extend)

**Interfaces:**
- Produces: `api.getBimiLogo(domain: string, selector?: string): Promise<Blob | null>` (`requestBlob` on `/api/Avatars/Bimi?domain=…&selector=…`, 404 gives null); `useSenderAvatarUrl(address, enabled, bimi?: { domain: string; selector?: string })`; `SenderAvatar` props gain `bimiDomain?`, `bimiSelector?` (primitives, so `MessageRow` stays memo-stable); the drafts branch of `MessageRow` passes none.

- [ ] **Step 1: Write failing tests:** (a) order: contact photo wins over a present `bimi`, BIMI wins over Gravatar, Gravatar when BIMI is 404; (b) a row without `bimiDomain` makes no `/api/Avatars/Bimi` call; (c) a BIMI error (429/500) falls through to Gravatar and then initials; (d) two rows with the same domain and selector make one request; (e) Gravatar is not requested while the BIMI query is pending; (f) `MessageRow` in a drafts folder passes no BIMI hint; (g) the object URL of a BIMI blob is revoked on unmount.
- [ ] **Step 2: Run** `cd src/frontend && npx vitest run src/modules/mail/list`; expected FAIL.
- [ ] **Step 3: Implement.** Query key `['avatar-bimi', domain, selector ?? 'default']`, `staleTime` 24 h; `fellThrough` for Gravatar becomes "no contact photo and no BIMI result"; `source` key distinguishes `bimi:<domain>`. Keep the hook readable: extract the Gravatar and BIMI queries into small local hooks if it passes ~60 lines.
- [ ] **Step 4: Run** `npx vitest run src/modules/mail/list && npm run typecheck && npm run lint`; expected PASS.
- [ ] **Step 5: Commit** `Show BIMI logos in the sender avatar`.

### Task 7: Hint, What's new, version, docs, geometry check

**Files:**
- Modify: `src/frontend/src/locales/{en,fr}/settings.json` (`avatars.hint`), `locales/{en,fr}/whatsNew.json`, `modules/whatsnew/catalog.ts`, `src/frontend/VERSION`, `package.json`, `package-lock.json` (2.6.0 to 2.7.0), `src/frontend/docs/architecture-mail.md`, `docs/superpowers/specs/2026-10-30-sender-avatars-design.md` (non-goal line now points to the BIMI spec), `src/frontend/probes/mobile-layout.html`
- Test: `modules/whatsnew/catalog.test.ts` (existing, must pass)

- [ ] **Step 1: Hint.** `avatars.hint` (en/fr) says the sender's address is checked against Gravatar and, for messages passing DMARC, the sender's domain is looked up for its logo, both through the server.
- [ ] **Step 2: What's new.** Entry `bimi-logos`, version `2.7.0`, same path and target as `sender-avatars` (`mail:show-avatars`, `href: '/settings/mail'`); `items.bimi-logos.title/body` in both languages. If the catalogue test forbids two entries on one target, report it rather than inventing a new anchor.
- [ ] **Step 3: Docs.** One line in `architecture-mail.md`; the sender-avatars spec's non-goal "BIMI logos" is replaced by a link to the BIMI spec.
- [ ] **Step 4: Geometry.** Add a BIMI-logo row (transparent PNG on the avatar box, default and wide skin) to `probes/mobile-layout.html`; measure in the browser that row height and avatar column are unchanged, and fix the box background/padding for transparent logos if they are not (a theme token, never a literal colour).
- [ ] **Step 5: Run** `cd src/frontend && npm test && npm run typecheck && npm run lint`, then `cd ../ && dotnet test`; expected all PASS.
- [ ] **Step 6: Commit** `What's new, hint and docs for BIMI logos`. Then stop: recette in the browser and the push are the owner's call.

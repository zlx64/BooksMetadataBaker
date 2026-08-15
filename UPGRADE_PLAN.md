# PrepKavitaPdf — Upgrade Plan

**Project**: ASP.NET Core 10 app that accepts PDF/EPUB uploads (500MB), aggregates metadata from AniList/Google Books/ComicVine, writes ebook metadata via Calibre `ebook-meta` (+ Ghostscript PDF repair), and emits a Kavita `series.json` + `.meta.json` sidecars.

Status: all findings verified against source (2026-08-15).

---

## P0 — Security & crash (fix first)

### 1. Path traversal via `Title`
- **Where**: `Services/UploadProcessingService.cs:170-175`
- `Sanitize()` only strips `Path.GetInvalidFileNameChars()`; `.` is valid on every platform, so `Title = "../../evil"` escapes the base folder (works on Windows *and* Linux/Docker). The endpoint is unauthenticated.
- **Fix**: `Path.GetFullPath` the result and assert it starts with the resolved base folder (`StartsWith(base + Path.DirectorySeparatorChar)`); reject `..` segments. Add a regression test.

### 2. Unauthenticated, unbounded upload
- **Where**: `Controllers/UploadController.cs:10-12`, `Startup/MiddlewareConfiguration.cs:20`
- No auth scheme configured (`UseAuthorization()` is a no-op), no rate limiting, 500MB writes to disk.
- **Fix**: at minimum add optional API-key auth (header check) + `AddRateLimiter` on the upload endpoint.

### 3. Regex crash → 500 on common filenames
- **Where**: `Services/UploadProcessingService.cs:69-75`
- Fallback regex `numMatch` has **one** capture group, but line 75 unconditionally reads `volMatch.Groups[2]`. Any filename with a bare number and no "vol" keyword (e.g. `Book 5.pdf`) throws `ArgumentOutOfRangeException` *outside* any try/catch → unhandled 500.
- **Fix**: track which match succeeded (`numMatch.Groups[1]` vs `volMatch.Groups[2]`).

---

## P1 — Correctness & reliability

### 4. Dead HttpClient config
- **Where**: `Startup/ServiceConfiguration.cs:22-24` vs `Startup/HttpClientConfiguration.cs:13-26`
- The *injected* clients (`AddHttpClient<IMetadataSource, X>`) use **hardcoded** BaseAddresses; the concrete-class registrations reading `PdfLibrary:*:BaseUrl` are never used — so the config keys are dead code, and `new Uri(configuration[...]!)` NREs at startup if a key is ever removed.
- **Fix**: single registration set that reads config with fallback defaults.

### 5. Pipe deadlock in process calls
- **Where**: `Services/CalibreMetadataUpdater.cs:28-30,70-72`, `Services/Helpers/PdfGhostscript.cs:36-43`
- `WaitForExit()` (or `WaitForExit(timeout)`) is called **before** `ReadToEnd()` on redirected stdout/stderr. If output exceeds the pipe buffer (gs stderr is verbose), the child blocks writing, the parent blocks waiting → deadlock that the timeout only partly covers.
- **Fix**: `proc.OutputDataReceived`/`ErrorDataReceived` events, or `await ReadToEndAsync()` before wait.

### 6. Blocking "async" + no ebook-meta timeout
- **Where**: `Services/EBookMetadataUpdater.cs:74-146`
- `DirectAttemptAsync`/`RepairAttemptAsync` are `Task.FromResult` wrappers around fully synchronous `WaitForExit()` calls (block thread-pool threads); `ct` is checked but never passed to the process; `ebook-meta` has **no timeout at all** (only gs has 120s) — a hung `ebook-meta` hangs the request forever.
- **Fix**: true async (`WaitForExitAsync(ct)`), pass `ct`, add a timeout for `ebook-meta`.

### 7. `LocalizedTitles` schema bug
- **Where**: `Services/KavitaMetadataWriter.cs:27`
- `metadata.Where(...)` serializes as an array of `{"Key":...,"Value":...}` objects, not a title→language object.
- **Fix**: build a `Dictionary<string,string>`.

### 8. `series.json` clobbering
- **Where**: `Services/KavitaMetadataWriter.cs:48`
- `File.WriteAllText` overwrites the series file; concurrent uploads of two volumes of the same series race and lose data.
- **Fix**: read-merge-write under a per-directory lock (or temp+rename), make writes atomic.

### 9. `GetUniqueEBookPath` is not unique
- **Where**: `Services/UploadProcessingService.cs:150-159`
- Deterministic name → existing files silently overwritten (`File.Create`, line 59); two concurrent uploads of the same volume write the same path simultaneously.
- **Fix**: per-title-folder lock, and either rename-with-suffix or explicit "replace" semantics in the API.

### 10. Orphaned child processes
- **Where**: `Services/Helpers/PdfGhostscript.cs:71-75`
- `Kill()` without `entireProcessTree: true` can leave a tree behind after timeout.

---

## P2 — Robustness & maintainability

### 11. Fragile argument building
- **Where**: `Services/CalibreMetadataUpdater.cs:88-129`, `Services/Helpers/MetadataHelpers.cs:7`
- Single-string args with manual `\"` escaping; `Escape()` only quotes when a space is present and never escapes embedded quotes → a path containing `"` breaks gs.
- **Fix**: `ProcessStartInfo.ArgumentList` everywhere (removes all manual escaping).

### 12. `ebook-meta` not configurable
- gs has `Tools:GhostscriptPath` but `ebook-meta` is PATH-only.
- **Fix**: add `Tools:EbookMetaPath` + a startup check that logs a clear warning if either tool is missing (today it fails per-upload).

### 13. Dead code
- `Services/AggregatedMetadataService.cs:9` (`preferredTitleVariant` read/logged, never used)
- `PdfLibrary:ProcessingBatchSize` (unused in `appsettings.json`)
- `ForceStripAttemptSuccess: false` hardcoded (`Services/UploadProcessingService.cs:99`)
- `UseAuthorization()` no-op (`Startup/MiddlewareConfiguration.cs:20`)

### 14. Merge policy
- **Where**: `Services/AggregatedMetadataService.cs:22-36`
- First-source-wins in DI registration order.
- **Fix**: make source order/priority configurable and prefer exact-title matches.

### 15. Error leakage
- **Where**: `Controllers/UploadController.cs:26`
- Returns raw internal error text (paths, exception messages) to clients.
- **Fix**: add an exception-handling middleware — log details, return generic message.

### 16. Culture-dependent parsing
- **Where**: `Services/UploadProcessingService.cs:163`
- `decimal.TryParse` without `CultureInfo.InvariantCulture`.

### 17. Docker hardening
- **Where**: `Dockerfile:9` (`chmod -R 0777 /data`), `Dockerfile:23` (`USER ${APP_UID:-root}`)
- **Fix**: use a fixed non-root UID/GID, `chown` instead of 0777.

### 18. Verify design assumption
- Confirm Kavita actually consumes a per-series `series.json` sidecar (no such format is officially documented); if it's for a custom workflow, document the schema (also fixes #7's schema).

---

## P3 — Quality

### 19. No tests
- CI's `dotnet test` step is gated on `hashFiles('**/*.Tests.csproj')` (`.github/workflows/ci.yml`), which is empty.
- **Fix**: add an xUnit project — unit tests for `MetadataHelpers` (`NormDate`, `ParseVolumeNumber`, `InferAgeRating`), volume-extraction (regression for #3), path containment (regression for #1), `BuildVolumeName`; integration test with a stubbed `IMetadataSource`.

### 20. Misc
- `UseHttpsRedirection()` (`Startup/MiddlewareConfiguration.cs:19`) warns on every request in the HTTP-only container — remove or gate it.
- Document env vars + sidecar format in README.

---

## Suggested order

1. **Week 1 (P0)**: #3 (one-line fix), #1, #2, #15 — small, unblocks safe deployment.
2. **Week 2 (P1)**: #11 (ArgumentList refactor) first, then #5, #6 on top of it (same code), then #4, #8, #9, #7, #10.
3. **Week 3 (P2/P3)**: #12, #13, #14, #16, #17, #19 (tests should start landing alongside P1 fixes as regressions), #18, #20.

## Progress

Completed 2026-08-15. Build: 0 warnings / 0 errors. Tests: 48/48 passing (`dotnet test`).

| # | Item | Status |
|---|------|--------|
| 1 | Path traversal via Title | done — `ResolveTitleFolder` containment check (`UploadProcessingService.cs`), regression tests |
| 2 | Auth + rate limiting | done — optional `X-Api-Key` (constant-time compare) + fixed-window rate limiter on `/api/upload` |
| 3 | Regex Groups[2] crash | done — extracted to `MetadataHelpers.ExtractVolumeToken`, regression test |
| 4 | Dead HttpClient config | done — single config-driven registration in `ServiceConfiguration` (with fallbacks); `HttpClientConfiguration.cs` deleted |
| 5 | Process pipe deadlock | done — `ProcessRunner` reads streams concurrently via `ReadToEndAsync` before awaiting exit |
| 6 | Blocking async / ebook-meta timeout | done — all process calls truly async, `ct` propagated, 120s timeout on ebook-meta |
| 7 | LocalizedTitles schema | done — serialized as a JSON object (dictionary) |
| 8 | series.json clobbering | done — per-directory lock, read-merge-write, temp+rename atomic write (`KavitaMetadataWriter`) |
| 9 | GetUniqueEBookPath overwrite | done — per-file `SemaphoreSlim` serializes concurrent uploads of the same file (replace semantics, documented in README) |
| 10 | Kill process tree | done — `Kill(entireProcessTree: true)` in `ProcessRunner.KillTree` |
| 11 | ArgumentList refactor | done — `ProcessStartInfo.ArgumentList` everywhere; manual quote escaping removed |
| 12 | ebook-meta path config + startup check | done — `Tools:EbookMetaPath` + startup warnings for missing gs/ebook-meta |
| 13 | Dead code cleanup | done — removed `preferredTitleVariant`, `ProcessingBatchSize`, `ForceStripAttemptSuccess`, `UseAuthorization()`, `MetadataHelpers.Escape` |
| 14 | Merge policy configurability | done — `Tools:SourceOrder` config + exact-title-match preference, tests |
| 15 | Error leakage middleware | done — outermost exception middleware (logs, generic 500/499); controller no longer returns raw errors |
| 16 | InvariantCulture parsing | done — `BuildVolumeNumber` uses invariant parsing; bonus: `ParseVolumeNumber` now case-insensitive |
| 17 | Docker hardening | done — non-root `app` user (APP_UID/APP_GID build args), `chown` instead of `chmod 0777` |
| 18 | Verify Kavita series.json assumption | done — verified: Kavita does NOT read series.json (discussion #3812); documented actual integration (filename + EPUB OPF) in README |
| 19 | xUnit test project | done — `Tests/BooksMetadataBaker.Tests` (48 tests: helpers, path containment, merge policy); excluded from main project globs |
| 20 | Misc (https redirection, docs) | done — `UseHttpsRedirection` gated on configured HTTPS port; README updated (env vars, tools, API key, rate limits, Kavita note, non-root Docker) |

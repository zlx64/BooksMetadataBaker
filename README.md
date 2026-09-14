# Books Metadata Baker

A modern ASP.NET Core web application for enriching Ebook (PDF/EPUB) and comic archive (CBZ/CBR/CB7/CBT) metadata by fetching information from multiple online sources and organizing files for media server applications like Kavita.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![License](https://img.shields.io/github/license/zlx64/BooksMetadataBaker)

## Overview

BooksMetadataBaker automatically enhances your Ebook collection by:
- Fetching metadata from **AniList**, **Google Books**, and **ComicVine**
- Embedding metadata into Ebooks with Calibre's `ebook-meta` (EPUB OPF + PDF info)
- Embedding `ComicInfo.xml` into comic archives (CBZ/CBR/CB7/CBT) for Kavita
- Repairing broken PDFs with Ghostscript when `ebook-meta` fails
- Writing a per-series `series.json` sidecar for Kavita-style workflows
- Creating comprehensive `.meta.json` sidecar files for tracking
- Supporting PDF, EPUB, and comic archive formats (CBZ, CBR, CB7, CBT)
- Organizing files by book type into configurable folder structures with Kavita-compatible volume/chapter/special naming
- Browsing and queueing files that already live on a mounted server volume (no upload from your own machine needed)

## Features

### Multi-Source Metadata Aggregation
- **AniList**: Manga and Light Novel metadata with multilingual title support
- **Google Books**: General book information and descriptions
- **ComicVine**: Comic book metadata with detailed attributes

### Metadata Management
- Automatic title normalization (English, Romaji, Native)
- Genre and tag aggregation
- Author/creator information
- Publication dates and volume numbers
- Age rating inference
- Description cleaning and formatting

### Comic & Manga Archives (CBZ / CBR / CB7 / CBT)
- `ComicInfo.xml` embedded at the archive root (ComicInfo v2.1 schema), only with fields that are actually known — nothing fabricated
- Existing `ComicInfo.xml` in the archive always wins over fetched metadata
- **CBZ**: edited in-process (entry order and stored/deflated compression preserved)
- **CBT**: extract + rebuild
- **CB7**: via 7-Zip (included in the Docker image; `Tools:SevenZipPath` resolves `7z` → `7zz`)
- **CBR**: read via 7-Zip; embedding requires WinRAR (`Tools:RarPath`) — when unavailable the file is still saved and organized, and the upload reports partial success
- **Raw containers**: `.zip` / `.7z` / `.rar` / `.tar` uploads are accepted and saved as their Kavita equivalent (`.cbz` / `.cb7` / `.cbr` / `.cbt`) — same container technology, so `ComicInfo.xml` is embedded in place without recompression
- Page counting with cover-image exclusion (`cover`/`!cover`/`folder` at archive root are not pages)
- Kavita-compatible naming: `<Title> - Volume N.ext`, `<Title> - Chapter N.ext`, and `<Title> SP##.ext` in a `Specials/` subfolder (disable-able) for volumes/chapters/specials
- Filename parsing handles volume/chapter markers, half-chapters (`b`), zero-padded and decimal numbers, and ignores 4-digit years

### File Organization
- Per-category folder configuration (absolute or relative paths)
- Kavita-style `series.json` generation (merged + atomic writes)
- Detailed processing logs per file
- Concurrent uploads are serialized per file to prevent write races

### Server-Volume File Source
- Queue files that already live on the server (e.g. a Docker volume mount) instead of uploading them from your own machine
- Source tabs in the UI: **This device** (upload) and **Server volume** (browse)
- Directory browser: subfolders first, dotfiles hidden, per-file size, and a `selectable` flag for accepted ebook/comic extensions
- Process a selected file in place: **copy** into the library by default, or **move** the original (source file is deleted; falls back to copy if the move is blocked by a locked file)
- Every path is contained to the configured root — path traversal is rejected
- Shares the upload endpoint's 500 MB per-file limit and rate limiting
- Enabled by default; the browsable root falls back to the library root (`ROOT_DIR`) when `ServerFiles:RootFolder` is empty

### User Interface
- Clean, modern web UI built with Vue.js (self-hosted, no CDN — system fonts only, works fully offline)
- Stepped workflow: **1. Book details** (title + segmented type picker with icons) → **2. Ebook files**
- Whole-window drag & drop (drop anywhere on the page) or browse, with client-side 500 MB size check
- File-source tabs: **This device** (drag & drop / browse local files) and **Server volume** (browse a mounted folder on the server, select files, choose copy or move)
- Per-file rows: format badge, predicted saved filename (`Title - Volume N.ext`), phase text
  (Queued → Uploading % → Baking → Baked), inline errors, and expandable applied-metadata details
  (attempts, direct/repair embed, Ghostscript repair, full metadata fields)
- Sticky bottom action bar: overall progress, baked/failed/running counts, elapsed time,
  **Bake files** (Ctrl+Enter), **Cancel**, **Retry failed**, **Clear finished**
- Server rate limiting (429) is handled automatically: throttled files wait (honoring the server's
  `Retry-After` header) and retry on their own, with a live countdown
- API key field (stored in the browser's localStorage), shown only when the server enforces `API_KEY` (probed via `GET /api/config`)
- Friendly error messages (401/429/400 surface the server's reason) + toast notifications
- Theme toggle (auto / light / dark, persisted), responsive design, reduced-motion support, screen-reader status announcements

## Getting Started

### Prerequisites

- **.NET 10.0 SDK** (for development)
- **Docker** (for containerized deployment)

### Docker Compose (Recommended)

```yaml
services:
  books-metadata-baker:
    image: ghcr.io/zlx64/booksmetadatabaker:latest
    restart: unless-stopped
    user: "1000:1000"
    environment:
      BOOK_DIR: /data/books/Novel
      LN_DIR: /data/books/Ranobe
      MANGA_DIR: /data/books/Manga
      COMIC_DIR: /data/books/Comics
      GOOGLE_BOOKS_KEY: ""
      COMIC_VINE_KEY: ""
    volumes:
      - /host/novels:/data/books/Novel:rw
      - /host/ranobe:/data/books/Ranobe:rw
      - /host/manga:/data/books/Manga:rw
      - /host/comics:/data/books/Comics:rw
    ports:
      - "8080:8080"
```

### Docker Run

Per-category mounts (recommended, `ROOT_DIR` not needed):

```bash
docker run -d \
  -p 8080:8080 \
  -v /host/novels:/data/novels \
  -v /host/ranobe:/data/ranobe \
  -v /host/manga:/data/manga \
  -v /host/comics:/data/comics \
  -e BOOK_DIR=/data/novels \
  -e LN_DIR=/data/ranobe \
  -e MANGA_DIR=/data/manga \
  -e COMIC_DIR=/data/comics \
  -e GOOGLE_BOOKS_KEY=YOUR_KEY \
  -e COMIC_VINE_KEY=YOUR_KEY \
  --name metadata-baker \
  books-metadata-baker
```

Single mount (all types under one folder, requires `ROOT_DIR`):

```bash
docker run -d \
  -p 8080:8080 \
  -v /host/books:/data/books \
  -e ROOT_DIR=/data/books \
  -e GOOGLE_BOOKS_KEY=YOUR_KEY \
  --name metadata-baker \
  books-metadata-baker
```

To also queue files that already live on a mounted volume (the **Server volume** source tab), mount an "incoming" folder and point `SERVER_FILES_DIR` at it:

```bash
docker run -d \
  -p 8080:8080 \
  -v /host/books:/data/books \
  -v /host/incoming:/srv/incoming \
  -e ROOT_DIR=/data/books \
  -e SERVER_FILES_DIR=/srv/incoming \
  --name metadata-baker \
  books-metadata-baker
```

The container runs as a **non-root user** (UID/GID 1000 by default). If your host bind mounts are owned by a different user, build with matching IDs:

```bash
docker build --build-arg APP_UID=$(id -u) --build-arg APP_GID=$(id -g) -t books-metadata-baker .
```

### Local Development

```bash
git clone https://github.com/zlx64/PrepKavitaPdf.git
cd PrepKavitaPdf
dotnet restore
dotnet run
```

The application will be available at `http://localhost:5000`.

## Configuration

All settings can be overridden via environment variables. Defaults are in `appsettings.json`.

| Environment Variable | Default | Description |
|---|---|---|
| `ROOT_DIR` | `/data/books` | Base folder (only needed if type dirs are relative) |
| `BOOK_DIR` | `Novel` | Folder for Book type (relative to `ROOT_DIR` or absolute) |
| `LN_DIR` | `Ranobe` | Folder for LightNovel type |
| `MANGA_DIR` | `Manga` | Folder for Manga type |
| `COMIC_DIR` | `Comics` | Folder for Comic type |
| `GOOGLE_BOOKS_KEY` | *(empty)* | Google Books API key |
| `COMIC_VINE_KEY` | *(empty)* | ComicVine API key |
| `API_KEY` | *(empty)* | When set, `/api/*` endpoints require the `X-Api-Key` header with this value (static UI stays open) |
| `MANGA_COMICS_ENABLED` | `true` | Enable comic archive (cbz/cbr/cb7/cbt) uploads; PDF/EPUB always work |
| `MANGA_COMICS_ALLOWED_EXTENSIONS` | `cbz,cbr,cb7,cbt,zip,rar,7z,tar` | Comma-separated archive extensions accepted for uploads (raw containers are saved as their Kavita equivalent) |
| `MANGA_COMICS_SPECIALS_SUBFOLDER` | `true` | Store specials (`<Title> SP##.ext`) in a `Specials/` subfolder instead of the title folder |
| `MANGA_COMICS_USE_CURLY_BRACE_YEAR` | `false` | When a trailing `(YYYY)` year is parsed from an archive filename, emit `{YYYY}` in the series folder (Kavita strips parentheses, not braces) |
| `SERVER_FILES_ENABLED` | `true` | Enable the "Server volume" source tab (browse/queue files from a mounted server volume) |
| `SERVER_FILES_DIR` | *(empty → `ROOT_DIR`)* | Folder to browse on the server, e.g. an "incoming" volume mount; empty falls back to the library root |

Directory values can be **relative** (subfolder of `ROOT_DIR`) or **absolute** (e.g. `/mnt/comics`). When all type directories are absolute, `ROOT_DIR` is not required.

### Tool Settings (`appsettings.json`)

```json
{
  "MangaComics": {
    "Enabled": true,
    "AllowedExtensions": "cbz,cbr,cb7,cbt,zip,rar,7z,tar",
    "SpecialsSubfolder": true,
    "UseCurlyBraceYear": false
  },
  "ServerFiles": {
    "Enabled": true,
    "RootFolder": ""
  },
  "Tools": {
    "SidecarMetadataEnabled": true,
    "GhostscriptEnabled": true,
    "GhostscriptPath": "gs",
    "EbookMetaPath": "ebook-meta",
    "SevenZipPath": "7z",
    "RarPath": "",
    "SourceOrder": "AniList,GoogleBooks,ComicVine"
  },
  "RateLimiting": {
    "UploadPermitLimit": 10,
    "UploadWindowSeconds": 60
  }
}
```

- `GhostscriptPath` / `EbookMetaPath` / `SevenZipPath` / `RarPath`: executable name or absolute path. A startup log warning is emitted if a tool cannot be found.
- `SevenZipPath`: used for CB7 read/write and CBR read. A bare name is resolved from `PATH`, trying the configured name first, then `7z`, then `7zz` (the `7zip` package, included in the Docker image, provides `7z` on Debian and `7zz` on newer distros — both are found).
- `RarPath`: WinRAR `rar` executable. Optional — when empty/unavailable, CBR files are still saved and organized, but `ComicInfo.xml` embedding is skipped and the upload is reported as a partial success.
- `MangaComics:Enabled`: set to `false` to reject comic archive uploads (PDF/EPUB keep working).
- `ServerFiles:Enabled`: set to `false` to hide the "Server volume" source tab and reject `/api/files` requests.
- `ServerFiles:RootFolder`: the browsable root for the server-volume source. When empty it falls back to `PdfLibrary:RootFolder` (`ROOT_DIR`).
- `SourceOrder`: priority order for metadata sources (comma-separated type names). A source whose returned `Title` exactly matches the searched title is always preferred.
- Uploads are rate-limited per client IP (default 10/minute). Rejected requests get HTTP 429 with a
  `Retry-After` header and a JSON body (`{"error":"Rate limit exceeded","retryAfterSeconds":N}`)
  so clients can back off and retry automatically.

## Usage

### Web Interface

1. Navigate to the application URL in your browser
2. Enter the **title** of the book/series and pick the **type** (Book, Light Novel, Manga, or Comic)
3. Add files — either the **This device** tab (drag & drop anywhere on the page, or browse) or the **Server volume** tab (browse a mounted folder on the server and select files). Accepted: PDF, EPUB, CBZ, CBR, CB7, CBT — or raw ZIP / RAR / 7Z / TAR, converted to their Kavita equivalent
4. If the server enforces `API_KEY`, paste it into the **API key** field (remembered in the browser)
5. Click **Bake files** (or press Ctrl+Enter)
6. Monitor per-file and overall progress in the sticky action bar; use **Cancel**, **Retry failed**,
   or **Clear finished** as needed
7. Expand any file row to see the applied metadata and processing details (attempts, Ghostscript repair, saved filename)

### API Endpoint

```http
POST /api/upload
Content-Type: multipart/form-data
X-Api-Key: <only required when API_KEY is configured>

Parameters:
- Title (required): Book/series title
- Type (required): Book | Comic | LightNovel | Manga
- file (required): PDF, EPUB, or comic archive file (.pdf, .epub, .cbz, .cbr, .cb7, .cbt, or raw .zip, .rar, .7z, .tar)
```

Ebooks are saved as `<Title> - Volume <N>.<ext>` (Kavita-compatible filename format) inside the type folder / title folder. Re-uploading the same volume replaces the existing file.

Comic archives are named from their filename: volume marker → `<Title> - Volume N.ext`, chapter marker (no volume) → `<Title> - Chapter N.ext`, no markers (or an `SP##` marker) → `<Title> SP##.ext` in a `Specials/` subfolder of the title folder (disable-able via `MangaComics:SpecialsSubfolder`).

**Response** (camelCase; `format` is a number: 0=Pdf, 1=Epub, 2=Cbz, 3=Cbr, 4=Cb7, 5=Cbt):

```json
{
  "files": [
    {
      "file": "My Series - Volume 1.cbz",
      "success": true,
      "errorMessage": null,
      "attempts": 1,
      "appliedMetadata": { "Title": "My Series", "Authors": "Author Name" },
      "directAttemptSuccess": false,
      "repairAttemptSuccess": false,
      "ghostscriptRan": false,
      "format": 2,
      "comicInfoWritten": true,
      "pageCount": 180
    }
  ],
  "metadata": { "Title": "My Series", "Authors": "Author Name" },
  "cancelled": false
}
```

For comic archives, `comicInfoWritten` reports whether `ComicInfo.xml` was embedded and `pageCount` the detected page count; the Calibre-related flags (`directAttemptSuccess`, `repairAttemptSuccess`, `ghostscriptRan`) stay `false` because archives never go through the `ebook-meta` pipeline. A CBR without the WinRAR tool succeeds with `comicInfoWritten: false` and an `errorMessage` explaining the skipped embedding.

### Server-Volume Endpoints

Browse a folder on the server (e.g. a mounted volume) and queue files from it, without uploading:

```http
GET /api/files?path=<relative path>
X-Api-Key: <only required when API_KEY is configured>
```

- `path` is optional — a `/`-separated path relative to the server-files root (`SERVER_FILES_DIR` / `ServerFiles:RootFolder`). Omit it to list the root.
- Returns `404` when the feature is disabled/unconfigured or the folder doesn't exist, and `400` for a path that escapes the root.

**Response** (subfolders first, then files; dotfiles hidden):

```json
{
  "path": "sub",
  "parent": "",
  "entries": [
    { "name": "Chapter 1", "path": "sub/Chapter 1", "isDir": true, "size": 0, "selectable": false },
    { "name": "My Series - Volume 1.cbz", "path": "sub/My Series - Volume 1.cbz", "isDir": false, "size": 184221, "selectable": true }
  ]
}
```

`parent` is the path one level up, or `null` at the root. `selectable` is `true` only for accepted ebook/comic extensions.

```http
POST /api/files/process
Content-Type: application/json
X-Api-Key: <only required when API_KEY is configured>

{
  "title": "My Series",
  "type": "Manga",
  "path": "sub/My Series - Volume 1.cbz",
  "moveOriginals": false
}
```

- `title` (required), `type` (required: `Book` | `Comic` | `LightNovel` | `Manga`), `path` (required, relative to the server-files root)
- `moveOriginals` (optional, default `false`): when `true` the source file is moved into the library and removed from the volume; when `false` it is copied. A move blocked by a locked source falls back to a copy.
- Rate-limited like the upload endpoint, with the same 500 MB per-file limit.
- The response shape matches `POST /api/upload` (`files`, `metadata`, `cancelled`).

## Technology Stack

- **Framework**: ASP.NET Core 10.0 / C# 14
- **Logging**: Serilog (Console + File)
- **Frontend**: Vue.js 3
- **Container**: Docker (Debian-based, includes Ghostscript + Calibre + 7-Zip)

## Metadata Pipeline

```
Upload file
  -> Fetch metadata from AniList / Google Books / ComicVine (parallel, priority-ordered merge)
  -> Normalize and aggregate metadata
  -> PDF/EPUB:
       -> Embed metadata with Calibre ebook-meta (title, series, index, authors, tags, ...)
       -> On failure for PDFs: Ghostscript repair pass, then retry ebook-meta
   -> Comic archives (cbz/cbr/cb7/cbt, or raw zip/rar/7z/tar saved as their Kavita equivalent):
        -> Parse volume/chapter/special from the filename
       -> Merge with any existing ComicInfo.xml in the archive (existing wins)
       -> Embed ComicInfo.xml at the archive root (CBZ in-process, CBT rebuild, CB7/CBR via 7-Zip)
  -> Write series.json + per-file .meta.json sidecar
  -> Save to organized folder structure (Kavita-compatible names)
```

## Output Files

Per title folder:
- **`series.json`**: Series-level metadata (merged across volumes, written atomically)
- **`Specials/`** (comic archives): subfolder for specials/one-shots/artbooks without volume or chapter numbers

Per Ebook file:
- **Processed Ebook**: Original file with embedded metadata
- **`[filename].meta.json`**: Detailed processing log with all fetched metadata

Per comic archive:
- **`ComicInfo.xml`** embedded at the archive root (Series, Number, Volume, credits, genres, PageCount, …)

## Kavita Integration

Kavita itself does **not** read a `series.json` sidecar (an open feature request: Kavita discussion #3812). It reads metadata from:

1. **File names** — this app saves files as `<Title> - Volume <N>.<ext>` / `<Title> - Chapter N.ext` / `<Title> SP##.ext`, which Kavita's filename parsers understand
2. **`ComicInfo.xml`** inside cb* archives — embedded at the archive root by this app
3. **Embedded OPF** inside EPUBs — `ebook-meta` rewrites this, so processed EPUBs carry full series metadata into Kavita

The `series.json` sidecar is therefore a forward-looking artifact for custom tooling (and a future Kavita feature). For PDFs, Kavita picks up series info from the filename; Ghostscript repair additionally makes damaged PDFs readable and indexable.

### Choosing a Kavita library type

The full reference lives in [`kavita-manga-comics-agent-guide.md`](kavita-manga-comics-agent-guide.md). The short decision:

| Your collection | Kavita library type | Why |
|---|---|---|
| Comics with complete `ComicInfo.xml` (Series + Volume) and ComicVine/Mylar issue-numbering conventions | **Comic** (strict) | Kavita composes the series name as `Series (Volume)` from metadata; folders like `Series (Vol Year) #01.cbz` are the convention |
| Comic-style files with partial or missing metadata | **Comic (Flexible)** | Legacy comic regex, not metadata-strict |
| Manga / webtoons / general volume-chapter naming | **Manga** | Matches this app's `<Series>/<Series> - v01.cbz` output directly |

Since this app embeds `ComicInfo.xml` with `Series` (and `Volume` when known), **Manga** type is the safe default for manga and light novels, and **Comic (Flexible)** works for comics even before the strict requirements are met. If you want the strict **Comic** type, make sure every file has both `Series` and `Volume` populated (this app writes both when the metadata source provides them).

### Structural rules (what the scanner expects)

- Every series lives in its own folder (nesting under publisher/genre folders is fine); never place archive files directly at the library root.
- Never split one series across two sibling folders with the same name.
- A year/disambiguator in a series folder name must use braces, not parentheses — `Series {2019}` keeps the year in the parsed name, `Series (2019)` strips it. Enable `MangaComics:UseCurlyBraceYear` to get `{YYYY}` when a year is parsed from an upload filename.
- Keep a `Specials/` subfolder inside a series folder for extras without volume/chapter numbers (this app creates it automatically; disable with `MangaComics:SpecialsSubfolder: false`).

### Pre-flight checklist (after a bulk upload/reorg)

- [ ] No archive files sit directly under the library root
- [ ] No series folder is duplicated as two sibling folders
- [ ] Every `ComicInfo.xml` is named exactly `ComicInfo.xml` and sits at the archive root (not nested)
- [ ] Files intended as Specials have no volume/chapter marker (or an explicit `SP##` marker)
- [ ] Series names that must keep a visible year use `{ }`, not `( )`
- [ ] For the strict Comic type: every file has both `Series` and `Volume` populated

Note: Kavita skips folders whose last-write time (minute granularity) hasn't changed since the last scan. If a move/rename doesn't show up after a scan, touch a file inside the folder to bump its mtime, then re-scan.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

MIT License - see the LICENSE file for details.

## Acknowledgments

- **AniList** for manga and light novel metadata
- **Google Books API** for book information
- **ComicVine** for comic book data
- **Ghostscript** for PDF metadata embedding
- **Calibre** for EPUB processing capabilities
- **7-Zip** for CB7/CBR archive handling

## Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/zlx64/BooksMetadataBaker).

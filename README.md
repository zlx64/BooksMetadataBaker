# Books Metadata Baker

A modern ASP.NET Core web application for enriching eBook metadata (PDF and EPUB) by fetching information from multiple online sources and organizing files for media server applications like Kavita.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![License](https://img.shields.io/github/license/zlx64/BooksMetadataBaker)

## Overview

BooksMetadataBaker automatically enhances your eBook collection by:
- Fetching metadata from **AniList**, **Google Books**, and **ComicVine**
- Embedding metadata into files with Calibre's `ebook-meta` (EPUB OPF + PDF info)
- Repairing broken PDFs with Ghostscript when `ebook-meta` fails
- Writing a per-series `series.json` sidecar for Kavita-style workflows
- Creating comprehensive `.meta.json` sidecar files for tracking
- Supporting both PDF and EPUB formats
- Organizing files by book type into configurable folder structures

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

### File Organization
- Per-category folder configuration (absolute or relative paths)
- Kavita-style `series.json` generation (merged + atomic writes)
- Detailed processing logs per file
- Concurrent uploads are serialized per file to prevent write races

### User Interface
- Clean, modern web UI built with Vue.js (self-hosted, no CDN — system fonts only, works fully offline)
- Stepped workflow: **1. Book details** (title + segmented type picker with icons) → **2. eBook files**
- Whole-window drag & drop (drop anywhere on the page) or browse, with client-side 500 MB size check
- Per-file rows: format badge, predicted saved filename (`Title - Volume N.ext`), phase text
  (Queued → Uploading % → Baking → Baked), inline errors, and expandable applied-metadata details
  (attempts, direct/repair embed, Ghostscript repair, full metadata fields)
- Sticky bottom action bar: overall progress, baked/failed/running counts, elapsed time,
  **Bake files** (Ctrl+Enter), **Cancel**, **Retry failed**, **Clear finished**
- "Baked metadata" summary card after the first successful response (authors, genres, publisher,
  description, source link, …)
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

Directory values can be **relative** (subfolder of `ROOT_DIR`) or **absolute** (e.g. `/mnt/comics`). When all type directories are absolute, `ROOT_DIR` is not required.

### Tool Settings (`appsettings.json`)

```json
{
  "Tools": {
    "SidecarMetadataEnabled": true,
    "GhostscriptEnabled": true,
    "GhostscriptPath": "gs",
    "EbookMetaPath": "ebook-meta",
    "SourceOrder": "AniList,GoogleBooks,ComicVine"
  },
  "RateLimiting": {
    "UploadPermitLimit": 10,
    "UploadWindowSeconds": 60
  }
}
```

- `GhostscriptPath` / `EbookMetaPath`: executable name or absolute path. A startup log warning is emitted if a tool cannot be found.
- `SourceOrder`: priority order for metadata sources (comma-separated type names). A source whose returned `Title` exactly matches the searched title is always preferred.
- Uploads are rate-limited per client IP (default 10/minute). Rejected requests get HTTP 429 with a
  `Retry-After` header and a JSON body (`{"error":"Rate limit exceeded","retryAfterSeconds":N}`)
  so clients can back off and retry automatically.

## Usage

### Web Interface

1. Navigate to the application URL in your browser
2. Enter the **title** of the book/series and pick the **type** (Book, Light Novel, Manga, or Comic)
3. Drag & drop (anywhere on the page) or browse one or more **PDF/EPUB files**
4. If the server enforces `API_KEY`, paste it into the **API key** field (remembered in the browser)
5. Click **Bake files** (or press Ctrl+Enter)
6. Monitor per-file and overall progress in the sticky action bar; use **Cancel**, **Retry failed**,
   or **Clear finished** as needed
7. Inspect the **Baked metadata** card and expand any file row to see the applied metadata
   and processing details (attempts, Ghostscript repair, saved filename)

### API Endpoint

```http
POST /api/upload
Content-Type: multipart/form-data
X-Api-Key: <only required when API_KEY is configured>

Parameters:
- Title (required): Book/series title
- Type (required): Book | Comic | LightNovel | Manga
- file (required): PDF or EPUB file
```

Files are saved as `<Title> - Volume <N>.<ext>` (Kavita-compatible filename format) inside the type folder / title folder. Re-uploading the same volume replaces the existing file.

**Response:**

```json
{
  "Files": [
    {
      "File": "processed_filename.pdf",
      "Success": true,
      "ErrorMessage": null,
      "Attempts": 2,
      "AppliedMetadata": { "Title": "Book Title", "Authors": "Author Name" },
      "DirectAttemptSuccess": true,
      "RepairAttemptSuccess": false,
      "GhostscriptRan": false,
      "Format": "Pdf"
    }
  ],
  "Metadata": { "Title": "Book Title", "Authors": "Author Name" },
  "Cancelled": false
}
```

## Technology Stack

- **Framework**: ASP.NET Core 10.0 / C# 14
- **Logging**: Serilog (Console + File)
- **Frontend**: Vue.js 3
- **Container**: Docker (Debian-based, includes Ghostscript + Calibre)

## Metadata Pipeline

```
Upload eBook file
  -> Fetch metadata from AniList / Google Books / ComicVine (parallel, priority-ordered merge)
  -> Normalize and aggregate metadata
  -> Embed metadata with Calibre ebook-meta (title, series, index, authors, tags, ...)
  -> On failure for PDFs: Ghostscript repair pass, then retry ebook-meta
  -> Write series.json + per-file .meta.json sidecar
  -> Save to organized folder structure
```

## Output Files

Per title folder:
- **`series.json`**: Series-level metadata (merged across volumes, written atomically)

Per eBook file:
- **Processed eBook**: Original file with embedded metadata
- **`[filename].meta.json`**: Detailed processing log with all fetched metadata

## Kavita Integration

Kavita itself does **not** read a `series.json` sidecar (an open feature request: Kavita discussion #3812). It reads metadata from:

1. **File names** — this app saves files as `<Title> - Volume <N>.<ext>`, which Kavita's filename parser understands
2. **`comicinfo.xml`** inside cb* archives
3. **Embedded OPF** inside EPUBs — `ebook-meta` rewrites this, so processed EPUBs carry full series metadata into Kavita

The `series.json` sidecar is therefore a forward-looking artifact for custom tooling (and a future Kavita feature). For PDFs, Kavita picks up series info from the filename; Ghostscript repair additionally makes damaged PDFs readable and indexable.

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

## Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/zlx64/BooksMetadataBaker).

# Books Metadata Baker

A modern ASP.NET Core web application for enriching eBook metadata (PDF and EPUB) by fetching information from multiple online sources and organizing files for media server applications like Kavita.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![License](https://img.shields.io/github/license/zlx64/BooksMetadataBaker)

## Overview

BooksMetadataBaker automatically enhances your eBook collection by:
- Fetching metadata from **AniList**, **Google Books**, and **ComicVine**
- Embedding metadata directly into PDF files using Ghostscript
- Generating Kavita-compatible metadata files (.json)
- Creating comprehensive sidecar metadata files for tracking
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
- Kavita series metadata generation
- Detailed processing logs per file
- Batch processing with concurrent upload support (up to 4 simultaneous)

### User Interface
- Clean, modern web UI built with Vue.js
- Real-time upload progress tracking
- Per-file status monitoring
- Metadata preview and details view
- Responsive design with custom styling

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

Directory values can be **relative** (subfolder of `ROOT_DIR`) or **absolute** (e.g. `/mnt/comics`). When all type directories are absolute, `ROOT_DIR` is not required.

### Tool Settings (`appsettings.json`)

```json
{
  "Tools": {
    "SidecarMetadataEnabled": true,
    "GhostscriptEnabled": true,
    "GhostscriptPath": "gs"
  }
}
```

## Usage

### Web Interface

1. Navigate to the application URL in your browser
2. Enter the **title** of the book/series
3. Select the **type** (Book, Comic, Light Novel, or Manga)
4. Choose one or more **PDF/EPUB files**
5. Click **Upload & Process**
6. Monitor progress and view metadata results

### API Endpoint

```http
POST /api/upload
Content-Type: multipart/form-data

Parameters:
- Title (required): Book/series title
- Type (required): Book | Comic | LightNovel | Manga
- file (required): PDF or EPUB file
```

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
      "GhostscriptRan": true,
      "Format": "Pdf"
    }
  ],
  "Metadata": { "Title": "Book Title", "Authors": "Author Name" },
  "Cancelled": false
}
```

## Technology Stack

- **Framework**: ASP.NET Core 10.0 / C# 12.0
- **Logging**: Serilog (Console + File)
- **Frontend**: Vue.js 3
- **Container**: Docker (Debian-based, includes Ghostscript + Calibre)

## Metadata Pipeline

```
Upload eBook file
  -> Fetch metadata from AniList / Google Books / ComicVine (parallel)
  -> Aggregate and normalize metadata
  -> Embed metadata into file (PDF via Ghostscript)
  -> Generate Kavita metadata JSON + sidecar file
  -> Save to organized folder structure
```

## Output Files

Per eBook file:
- **Processed eBook**: Original file with embedded metadata
- **`[filename].metadata.json`**: Kavita series metadata
- **`[filename].sidecar.json`**: Detailed processing log with all metadata

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

# Books Metadata Baker 📚✨🍞

A modern ASP.NET Core web application for enriching eBook metadata (PDF and EPUB) by fetching information from multiple online sources and organizing files for media server applications like Kavita.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![License](https://img.shields.io/github/license/zlx64/BooksMetadataBaker)

## 📖 Overview

BooksMetadataBaker automatically enhances your eBook collection by:
- Fetching metadata from **AniList**, **Google Books**, and **ComicVine**
- Embedding metadata directly into PDF files using Ghostscript
- Generating Kavita-compatible metadata files (.json)
- Creating comprehensive sidecar metadata files for tracking
- Supporting both PDF and EPUB formats
- Organizing files by book type into configurable folder structures

## ✨ Features

### 🔍 Multi-Source Metadata Aggregation
- **AniList**: Manga and Light Novel metadata with multilingual title support
- **Google Books**: General book information and descriptions
- **ComicVine**: Comic book metadata with detailed attributes

### 📚 eBook Format Support
- **PDF**: Full metadata embedding using Ghostscript
- **EPUB**: Metadata extraction and sidecar generation

### 🎯 Metadata Management
- Automatic title normalization (English, Romaji, Native)
- Genre and tag aggregation
- Author/creator information
- Publication dates and volume numbers
- Age rating inference
- Description cleaning and formatting

### 🗂️ File Organization
- Configurable folder structure by book type
- Kavita series metadata generation
- Detailed processing logs per file
- Batch processing with concurrent upload support (up to 4 simultaneous)

### 🖥️ User Interface
- Clean, modern web UI built with Vue.js
- Real-time upload progress tracking
- Per-file status monitoring
- Metadata preview and details view
- Responsive design with custom styling

## 🚀 Getting Started

### Prerequisites

- **.NET 10.0 SDK** (for development)
- **Docker** (for containerized deployment)
- **Ghostscript** (automatically installed in Docker)
- **Calibre** (automatically installed in Docker)

### Installation

#### Option 1: Docker (Recommended)

```bash
# Build the Docker image
docker build -t books-metadata-baker .

# Run the container
docker run -d \
  -p 8080:8080 \
  -v /path/to/your/books:/data/books \
  -e PdfLibrary__RootFolder=/data/books \
  --name metadata-baker \
  books-metadata-baker
```

#### Option 1b: Docker Compose

Create a `docker-compose.yml` file:

```yaml
services:
  books-metadata-baker:
    image: ghcr.io/zlx64/booksmetadatabaker:latest
    ports:
      - "8080:8080"
    volumes:
      - ./data/books:/data/books
    environment:
      - PdfLibrary__RootFolder=/data/books
    restart: unless-stopped
```

Then run:

```bash
docker compose up -d
```

#### Option 2: Local Development

```bash
# Clone the repository
git clone https://github.com/zlx64/PrepKavitaPdf.git
cd PrepKavitaPdf

# Restore dependencies
dotnet restore

# Run the application
dotnet run
```

The application will be available at `http://localhost:5000` (or the port specified in your launch settings).

## ⚙️ Configuration

Configure the application through `appsettings.json`:

### Library Settings

```json
{
  "PdfLibrary": {
    "RootFolder": "/data/books",
    "ProcessingBatchSize": 4,
    "TypeFolders": {
      "Book": "Novel",
      "LightNovel": "Ranobe",
      "Manga": "Manga",
      "Comic": "Comics"
    }
  }
}
```

### API Configuration

```json
{
  "PdfLibrary": {
    "AniList": {
      "BaseUrl": "https://graphql.anilist.co"
    },
    "GoogleBooks": {
      "BaseUrl": "https://www.googleapis.com/books/v1/volumes",
      "ApiKey": "YOUR_API_KEY"
    },
    "ComicVine": {
      "BaseUrl": "https://comicvine.gamespot.com/api",
      "ApiKey": "YOUR_API_KEY"
    }
  }
}
```

### Tool Settings

```json
{
  "Tools": {
    "SidecarMetadataEnabled": true,
    "GhostscriptEnabled": true,
    "GhostscriptPath": "gs",
    "PreferredTitleVariant": "English"
  }
}
```

## 📝 Usage

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
      "Message": "Success",
      "Errors": null
    }
  ],
  "Metadata": {
    "Title": "Book Title",
    "TitleEnglish": "English Title",
    "Authors": "Author Name",
    "Description": "Book description...",
    "Genres": "Fantasy, Adventure",
    "PublishedDate": "2024-01-01"
  },
  "Cancelled": false
}
```

## 🔧 Technology Stack

- **Framework**: ASP.NET Core 10.0
- **Language**: C# 12.0
- **Logging**: Serilog with Console and File sinks
- **HTTP Client**: IHttpClientFactory
- **Caching**: In-Memory Cache
- **Frontend**: Vue.js 3, Vanilla JavaScript
- **Container**: Docker with Debian-based runtime
- **External Tools**: Ghostscript, Calibre

## 📊 Metadata Pipeline

```
1. Upload eBook file
   ↓
2. Fetch metadata from multiple sources (parallel)
   ├── AniList (Manga/Light Novels)
   ├── Google Books (General books)
   └── ComicVine (Comics)
   ↓
3. Aggregate and normalize metadata
   ↓
4. Embed metadata into file (PDF via Ghostscript)
   ↓
5. Generate Kavita metadata JSON
   ↓
6. Write sidecar metadata file
   ↓
7. Move to organized folder structure
```

## 🛠️ Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Code Structure Guidelines

- Extension methods for configuration (Startup folder)
- Global usings for common namespaces
- Async/await patterns throughout
- Dependency injection for all services
- Structured logging with Serilog

## 📄 Output Files

### Per eBook File
- **Processed eBook**: Original file with embedded metadata
- **[filename].metadata.json**: Kavita series metadata
- **[filename].sidecar.json**: Detailed processing log with all metadata

### Sidecar Example
```json
{
  "OriginalFileName": "book.pdf",
  "ProcessedFileName": "processed_book.pdf",
  "Success": true,
  "ProcessingTimestamp": "2024-01-15T10:30:00Z",
  "Metadata": {
    "Title": "Book Title",
    "Authors": "Author Name"
  },
  "MetadataApplied": true,
  "GhostscriptRan": true,
  "Errors": null
}
```

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📜 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Acknowledgments

- **AniList** for manga and light novel metadata
- **Google Books API** for book information
- **ComicVine** for comic book data
- **Ghostscript** for PDF metadata embedding
- **Calibre** for EPUB processing capabilities

## 📞 Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/zlx64/BooksMetadataBaker).

---

Made with ❤️ for the book lovers and digital library enthusiasts

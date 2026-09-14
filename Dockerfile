# ---------------------------------------------------------
# Base runtime image
# ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ARG APP_UID=1000
ARG APP_GID=1000
# Calibre's dependencies may pre-create an 'app' group, so create idempotently
# 7zip provides 7z (newer distros ship 7zz; Tools:SevenZipPath tries 7z then 7zz).
# Used for CB7 read/write and CBR read. CBR ComicInfo embedding stays opt-in via Tools:RarPath.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ghostscript calibre 7zip \
    && rm -rf /var/lib/apt/lists/*
RUN (getent group app >/dev/null || groupadd --gid ${APP_GID} app) \
    && (getent passwd app >/dev/null || useradd --uid ${APP_UID} --gid app --create-home app) \
    && mkdir -p /data/books \
    && chown -R ${APP_UID}:${APP_GID} /data
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Configuration via environment variables (all optional, defaults in appsettings.json):
#   ROOT_DIR           - base folder for books (default: /data/books, only needed if type dirs are relative)
#   BOOK_DIR           - folder for Book type (default: "Novel" under ROOT_DIR)
#   LN_DIR             - folder for LightNovel (default: "Ranobe")
#   MANGA_DIR          - folder for Manga type (default: "Manga")
#   COMIC_DIR          - folder for Comic type (default: "Comics")
#   GOOGLE_BOOKS_KEY   - Google Books API key
#   COMIC_VINE_KEY     - ComicVine API key
#   API_KEY            - optional; when set, /api endpoints require the X-Api-Key header
#   MANGA_COMICS_ENABLED            - enable comic archive uploads (default: true)
#   MANGA_COMICS_ALLOWED_EXTENSIONS - comma-separated archive extensions (default: cbz,cbr,cb7,cbt,zip,rar,7z,tar; raw containers are saved as their Kavita equivalent)
#   MANGA_COMICS_SPECIALS_SUBFOLDER - store specials in Specials/ subfolder (default: true)
#   MANGA_COMICS_USE_CURLY_BRACE_YEAR - emit {YYYY} instead of (YYYY) for parsed years (default: false)
#   SERVER_FILES_ENABLED - browse/queue files from a mounted volume instead of uploading (default: true)
#   SERVER_FILES_DIR     - folder to browse, e.g. an "incoming" volume (default: ROOT_DIR / PdfLibrary:RootFolder)
#   SEVEN_ZIP_PATH     - 7-Zip executable for CB7/CBR (default: 7z, falls back to 7zz)
#   RAR_PATH           - optional WinRAR executable for CBR ComicInfo embedding
# Directory values can be relative (subfolder of ROOT_DIR) or absolute (e.g. /mnt/comics)
# If all type dirs are absolute, ROOT_DIR is not needed
# Build args: APP_UID / APP_GID (default 1000:1000). Match the owner of host bind mounts:
#   docker build --build-arg APP_UID=$(id -u) --build-arg APP_GID=$(id -g) .
USER ${APP_UID}:${APP_GID}

# ---------------------------------------------------------
# Build stage
# ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore as distinct layer to leverage Docker cache
COPY ["BooksMetadataBaker.csproj", "."]
RUN dotnet restore "BooksMetadataBaker.csproj"

# Copy everything and build
COPY . .
RUN dotnet build "BooksMetadataBaker.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/build

# ---------------------------------------------------------
# Publish stage
# ---------------------------------------------------------
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BooksMetadataBaker.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------
# Final image
# ---------------------------------------------------------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Declare container path; host binding MUST be provided when running:
# Single mount (all types under one folder, ROOT_DIR needed for relative type dirs):
#   docker run -e ROOT_DIR=/data/books -v /host/path/books:/data/books <image>
# Per-category mounts (absolute paths, ROOT_DIR not needed):
#   docker run \
#     -v /host/path/novels:/data/novels \
#     -v /host/path/ranobe:/data/ranobe \
#     -v /host/path/manga:/data/manga \
#     -v /host/path/comics:/data/comics \
#     -e BOOK_DIR=/data/novels \
#     -e LN_DIR=/data/ranobe \
#     -e MANGA_DIR=/data/manga \
#     -e COMIC_DIR=/data/comics \
#     -e GOOGLE_BOOKS_KEY=YOUR_KEY \
#     -e COMIC_VINE_KEY=YOUR_KEY \
#     <image>
VOLUME ["/data/books"]
ENTRYPOINT ["dotnet", "BooksMetadataBaker.dll"]

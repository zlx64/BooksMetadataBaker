# ---------------------------------------------------------
# Base runtime image
# ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update \
    && apt-get install -y --no-install-recommends ghostscript calibre \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/books \
    && chmod -R 0777 /data
WORKDIR /app
EXPOSE 8080 8081
# Configuration via environment variables (all optional, defaults in appsettings.json):
#   ROOT_DIR           - base folder for books (default: /data/books, only needed if type dirs are relative)
#   BOOK_DIR           - folder for Book type (default: "Novel" under ROOT_DIR)
#   LN_DIR             - folder for LightNovel (default: "Ranobe")
#   MANGA_DIR          - folder for Manga type (default: "Manga")
#   COMIC_DIR          - folder for Comic type (default: "Comics")
#   GOOGLE_BOOKS_KEY   - Google Books API key
#   COMIC_VINE_KEY     - ComicVine API key
# Directory values can be relative (subfolder of ROOT_DIR) or absolute (e.g. /mnt/comics)
# If all type dirs are absolute, ROOT_DIR is not needed
ARG APP_UID
USER ${APP_UID:-root}

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

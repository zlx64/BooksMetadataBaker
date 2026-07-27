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
ENV PdfLibrary__RootFolder=/data/books
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
# docker run -v D:/data/books:/data/books -e PdfLibrary__RootFolder=/data/books <image>
VOLUME ["/data/books"]
ENTRYPOINT ["dotnet", "BooksMetadataBaker.dll"]

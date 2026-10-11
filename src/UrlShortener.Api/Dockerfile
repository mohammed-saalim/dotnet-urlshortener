# Build context is the repository root (azure.yaml: docker.context), because restore and build need the root
# global.json, Directory.Build.props, Directory.Packages.props and .editorconfig. See ADR-0006.
#   docker build -f src/UrlShortener.Api/Dockerfile -t urlshortener-api .

# SDK pinned to global.json's version: analyzers differ between SDK patches and warnings are errors (CLAUDE.md section 9).
FROM mcr.microsoft.com/dotnet/sdk:10.0.101 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/UrlShortener.Api/UrlShortener.Api.csproj src/UrlShortener.Api/
RUN dotnet restore src/UrlShortener.Api/UrlShortener.Api.csproj
COPY src/UrlShortener.Api/ src/UrlShortener.Api/
RUN dotnet publish src/UrlShortener.Api/UrlShortener.Api.csproj -c Release --no-restore -o /app/publish -p:UseAppHost=false

# Runtime floats on the 10.0 patch line so a rebuild picks up security patches. The image listens on 8080
# (ASPNETCORE_HTTP_PORTS) and defines the non-root "app" user; the numeric UID lets a platform verify non-root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "UrlShortener.Api.dll"]

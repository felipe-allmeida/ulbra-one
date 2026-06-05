# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (copy only what the restore graph needs first, for layer caching).
# global.json pins the SDK (10.0.100) — copy it before restore so the build honors it.
COPY global.json Directory.Build.props Directory.Packages.props Ulbra.One.slnx ./
COPY src/ src/
RUN dotnet restore src/API/Ulbra.One.Api/Ulbra.One.Api.csproj

# Publish
RUN dotnet publish src/API/Ulbra.One.Api/Ulbra.One.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ulbra.One.Api.dll"]

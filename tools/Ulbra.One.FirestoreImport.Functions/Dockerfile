# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore graph: root MSBuild props + the tools/ tree (the Functions host references only the
# core lib under tools/, not the monolith under src/).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY tools/ tools/
RUN dotnet restore tools/Ulbra.One.FirestoreImport.Functions/Ulbra.One.FirestoreImport.Functions.csproj
RUN dotnet publish tools/Ulbra.One.FirestoreImport.Functions/Ulbra.One.FirestoreImport.Functions.csproj \
    -c Release -o /app/publish --no-restore

# Azure Functions isolated-worker base image. Confirm the net10 tag is published before building;
# if only a preview tag exists, pin it here (mirrors the net10 Worker packages caveat).
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0 AS runtime
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true
WORKDIR /home/site/wwwroot
COPY --from=build /app/publish .
EXPOSE 80

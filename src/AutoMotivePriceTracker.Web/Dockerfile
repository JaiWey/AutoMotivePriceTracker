# API + scrapers. Build from the repo root:
#   docker build -f src/AutoMotivePriceTracker.Web/Dockerfile -t autoprice-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first so the layer is cached until a project file changes
COPY src/AutoMotivePriceTracker.Domain/*.csproj src/AutoMotivePriceTracker.Domain/
COPY src/AutoMotivePriceTracker.Application/*.csproj src/AutoMotivePriceTracker.Application/
COPY src/AutoMotivePriceTracker.Infrastructure/*.csproj src/AutoMotivePriceTracker.Infrastructure/
COPY src/AutoMotivePriceTracker.Web/*.csproj src/AutoMotivePriceTracker.Web/
RUN dotnet restore src/AutoMotivePriceTracker.Web
COPY src src
RUN dotnet publish src/AutoMotivePriceTracker.Web -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Chromium for the Playwright scrapers, using the Node driver bundled in the publish output
# (the PowerShell installer from CLAUDE.md is not available in this image).
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN ./.playwright/node/linux-x64/node ./.playwright/package/cli.js install --with-deps chromium \
    && chmod -R a+rX /ms-playwright /app/.playwright     && chmod a+x /app/.playwright/node/linux-x64/node \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "AutoMotivePriceTracker.Web.dll"]

# ==============================================================================
# Multi-stage Dockerfile for AI Client Hunter (Frontend + Backend + Puppeteer)
# Works seamlessly on Render, Railway, Koyeb, Fly.io, or VPS
# ==============================================================================

# --- Stage 1: Build React Frontend ---
FROM node:20-alpine AS build-client
WORKDIR /src/client
COPY client/package*.json ./
RUN npm install
COPY client/ ./
RUN npm run build

# --- Stage 2: Build .NET 9 API Backend ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-server
WORKDIR /src
COPY server/AiClientHunter.Api/*.csproj server/AiClientHunter.Api/
RUN dotnet restore server/AiClientHunter.Api/AiClientHunter.Api.csproj
COPY server/ server/
RUN dotnet publish server/AiClientHunter.Api/AiClientHunter.Api.csproj -c Release -o /app/publish

# Copy React build to wwwroot so backend serves SPA frontend directly
COPY --from=build-client /src/client/dist /app/publish/wwwroot

# --- Stage 3: Runtime with Node.js & Chromium for Scrapers ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install Chromium, Node.js, and npm for Puppeteer scrapers
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    chromium \
    fonts-liberation \
    nodejs \
    npm \
    && rm -rf /var/lib/apt/lists/*


# Copy backend binaries and frontend assets
COPY --from=build-server /app/publish .

# Copy scrapers and install dependencies
COPY scraper /app/scraper
WORKDIR /app/scraper
RUN npm install --omit=dev
WORKDIR /app

# Configure persistent directory for SQLite database
RUN mkdir -p /app/data

# Environment configuration
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:10000 \
    PORT=10000 \
    USE_SQLITE=true \
    SQLITE_PATH=/app/data/clienthunter.db \
    SCRAPER_DIR=/app/scraper \
    PUPPETEER_SKIP_CHROMIUM_DOWNLOAD=true \
    PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium

EXPOSE 10000

ENTRYPOINT ["dotnet", "AiClientHunter.Api.dll"]

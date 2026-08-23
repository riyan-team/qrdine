# QrDine — single image: builds the React frontend AND the .NET API, then serves
# both from one ASP.NET Core app (frontend from wwwroot, API under /api, SignalR
# under /hub). Build context = repository root.

# ---- 1. build the React frontend ----
FROM node:20-alpine AS frontend
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build              # → /web/dist

# ---- 2. build & publish the API ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY QrDine.sln ./
COPY src/ ./src/
# Publish only the API (auto-restores its project graph; skips the test project).
RUN dotnet publish src/QrDine.Api/QrDine.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- 3. runtime: API + built frontend in wwwroot ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=frontend /web/dist ./wwwroot
# Fallback port for local `docker run`; on Render the app binds $PORT (see Program.cs).
ENV ASPNETCORE_URLS=http://+:8080
# Don't watch appsettings.json for changes — the file watchers exhaust the small
# inotify limit on the container and crash startup ("inotify instances reached").
ENV DOTNET_hostBuilder__reloadConfigOnChange=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "QrDine.Api.dll"]

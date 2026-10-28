# One image = API + React UI (served from wwwroot), so the demo is a single URL.

# ---- 1. frontend ----
FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
# Same origin as the API -> relative URLs, no CORS.
ENV VITE_API_BASE_URL=""
RUN npm run build

# ---- 2. backend ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/RagKb.Api/RagKb.Api.csproj RagKb.Api/
RUN dotnet restore RagKb.Api/RagKb.Api.csproj
COPY backend/RagKb.Api/ RagKb.Api/
RUN dotnet publish RagKb.Api/RagKb.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- 3. runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=8080
EXPOSE 8080
COPY --from=api /app/publish .
COPY --from=web /web/dist ./wwwroot
USER app
ENTRYPOINT ["dotnet", "RagKb.Api.dll"]

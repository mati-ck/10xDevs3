# Dockerfile for 10xNotes (.NET 10 Blazor Web App)
# Build pack in Coolify: Dockerfile, app port: 8080
# NOTE: ENTRYPOINT assumes output dll is 10xnotes.dll (from 10xnotes.csproj).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish 10xnotes.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl is required for the HEALTHCHECK below — the aspnet image ships without it,
# which is exactly why Coolify's container health check failed and broke routing.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Probe the app's own /health endpoint over the in-container HTTP port.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1
# Run as the image's built-in non-root user.
USER $APP_UID
ENTRYPOINT ["dotnet", "10xnotes.dll"]

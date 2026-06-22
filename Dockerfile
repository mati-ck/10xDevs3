# Dockerfile for 10xNotes (.NET 10 Blazor Web App)
# Build pack in Coolify: Dockerfile, app port: 8080
# NOTE: ENTRYPOINT assumes output dll is 10xnotes.dll (from 10xnotes.csproj).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "10xnotes.dll"]

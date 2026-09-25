# Multi-stage Dockerfile for AikaEngine (.NET 9 / 10 Minimal API)
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview-alpine AS build
WORKDIR /app

# Copy solution and project definitions
COPY AikaEngine.sln ./
COPY src/AikaEngine.Domain/AikaEngine.Domain.csproj src/AikaEngine.Domain/
COPY src/AikaEngine.Application/AikaEngine.Application.csproj src/AikaEngine.Application/
COPY src/AikaEngine.WebApi/AikaEngine.WebApi.csproj src/AikaEngine.WebApi/
COPY tests/AikaEngine.UnitTests/AikaEngine.UnitTests.csproj tests/AikaEngine.UnitTests/

# Restore dependencies
RUN dotnet restore

# Copy all sources
COPY src/ src/
COPY tests/ tests/

# Execute unit tests inside the build container
RUN dotnet test tests/AikaEngine.UnitTests/AikaEngine.UnitTests.csproj -c Release --no-restore

# Publish the WebApi
RUN dotnet publish src/AikaEngine.WebApi/AikaEngine.WebApi.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview-alpine AS runtime
WORKDIR /app
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Development

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AikaEngine.WebApi.dll"]

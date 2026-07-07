# Stage 1 — build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy project files first to leverage layer caching on restore
COPY Directory.Build.props ./
COPY src/Ordinis.Domain/Ordinis.Domain.csproj               src/Ordinis.Domain/
COPY src/Ordinis.Application/Ordinis.Application.csproj     src/Ordinis.Application/
COPY src/Ordinis.Infrastructure/Ordinis.Infrastructure.csproj src/Ordinis.Infrastructure/
COPY src/Ordinis.Api/Ordinis.Api.csproj                     src/Ordinis.Api/

# Restore only the API project — test projects are not needed in the image
RUN dotnet restore src/Ordinis.Api/Ordinis.Api.csproj

COPY src/ src/

RUN dotnet publish src/Ordinis.Api/Ordinis.Api.csproj \
    --configuration Release \
    --output /app/publish

# Stage 2 — runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# .NET 10 ASP.NET images default to user 'app' (UID 1654, non-root)
USER app

EXPOSE 8080

ENTRYPOINT ["dotnet", "Ordinis.Api.dll"]

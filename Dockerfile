# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/OsService.Api/OsService.Api.csproj                     src/OsService.Api/
COPY src/OsService.Application/OsService.Application.csproj     src/OsService.Application/
COPY src/OsService.Domain/OsService.Domain.csproj               src/OsService.Domain/
COPY src/OsService.Infrastructure/OsService.Infrastructure.csproj src/OsService.Infrastructure/

RUN dotnet restore src/OsService.Api/OsService.Api.csproj

COPY src/ src/
RUN dotnet publish src/OsService.Api/OsService.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "OsService.Api.dll"]

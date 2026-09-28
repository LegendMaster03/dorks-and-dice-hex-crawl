FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["src/HexCrawl.Domain/HexCrawl.Domain.csproj", "src/HexCrawl.Domain/"]
COPY ["src/HexCrawl.Application/HexCrawl.Application.csproj", "src/HexCrawl.Application/"]
COPY ["src/HexCrawl.Infrastructure/HexCrawl.Infrastructure.csproj", "src/HexCrawl.Infrastructure/"]
COPY ["tools/HexCrawl.PersistenceMigration/HexCrawl.PersistenceMigration.csproj", "tools/HexCrawl.PersistenceMigration/"]
RUN dotnet restore "tools/HexCrawl.PersistenceMigration/HexCrawl.PersistenceMigration.csproj"

COPY . .
RUN dotnet publish "tools/HexCrawl.PersistenceMigration/HexCrawl.PersistenceMigration.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HexCrawl.PersistenceMigration.dll"]

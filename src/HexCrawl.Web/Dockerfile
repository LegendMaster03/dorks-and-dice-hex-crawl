FROM node:24-alpine AS frontend
WORKDIR /src/src/HexCrawl.Web/Client
COPY ["src/HexCrawl.Web/Client/package.json", "./"]
COPY ["src/HexCrawl.Web/Client/tsconfig.json", "./"]
COPY ["src/HexCrawl.Web/Client/vite.config.ts", "./"]
COPY ["src/HexCrawl.Web/Client/src", "./src"]
COPY ["src/HexCrawl.Web/Client/test", "./test"]
RUN npm install --no-audit --no-fund && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["src/HexCrawl.Domain/HexCrawl.Domain.csproj", "src/HexCrawl.Domain/"]
COPY ["src/HexCrawl.Application/HexCrawl.Application.csproj", "src/HexCrawl.Application/"]
COPY ["src/HexCrawl.Infrastructure/HexCrawl.Infrastructure.csproj", "src/HexCrawl.Infrastructure/"]
COPY ["src/HexCrawl.Web/HexCrawl.Web.csproj", "src/HexCrawl.Web/"]
RUN dotnet restore "src/HexCrawl.Web/HexCrawl.Web.csproj"
COPY . .
COPY --from=frontend /src/src/HexCrawl.Web/wwwroot ./src/HexCrawl.Web/wwwroot
RUN dotnet publish "src/HexCrawl.Web/HexCrawl.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false -p:BuildClient=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
RUN mkdir -p /data
VOLUME ["/data"]
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HexCrawl.Web.dll"]

# Dorks & Dice Hex Crawl

Dorks & Dice Hex Crawl provides continuous-overworld authoring, mathematical hex geometry, semantic spatial features, crawl/session runtime state, player-knowledge separation, procedure snapshots, and Embedded Module v2 hosting compatibility.

## Stack

- .NET 10 / ASP.NET Core
- PostgreSQL 18-compatible persistence through Npgsql and hand-written SQL
- TypeScript 7 / Vite 8
- Canvas 2D
- Filesystem-backed raster/source-map assets
- Docker
- xUnit and Node test runners

## Local development

Hex Crawl requires PostgreSQL for structured runtime persistence. Copy `.env.example` values into your local environment and change the development password as appropriate. The normal application does not fall back to SQLite.

```bash
export ConnectionStrings__HexCrawl='Host=localhost;Port=5432;Database=hex_crawl;Username=hex_crawl;Password=change-me'
cd src/HexCrawl.Web/Client
npm install
npm run build
cd ../../..
dotnet run --project src/HexCrawl.Web
```

`MapAssets__RootPath` may be set explicitly; otherwise standalone development uses the web project's `data/assets` directory. Production keeps map binaries at `/data/assets`; PostgreSQL stores only structured state and source-map metadata.

## Validation

The normal CI suite runs persistence and HTTP tests against PostgreSQL, builds the Docker image, and exercises restart persistence against an ephemeral PostgreSQL instance.

```bash
cd src/HexCrawl.Web/Client && npm run build
cd ../../..
dotnet test dorks-and-dice-hex-crawl.slnx -p:BuildClient=false
docker build -t dorks-and-dice-hex-crawl:test -f src/HexCrawl.Web/Dockerfile .
```

For the one-time production SQLite → PostgreSQL migration and rollback procedure, see `docs/postgresql-persistence.md`. See `docs/architecture.md`, `docs/design-references.md`, and `docs/tool-hosting.md` for broader architecture and hosting decisions.

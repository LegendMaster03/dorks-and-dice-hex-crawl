# Dorks & Dice Hex Crawl

Dorks & Dice Hex Crawl is the deployable tool for generalized Tile Crawl world authoring and expedition play. The current spatial engine uses periodic polygonal tilings rather than a separate hex-only traversal runtime. It retains semantic spatial features, source-map assets, expedition/runtime state, player-knowledge separation, pinned procedure snapshots, and Embedded Module v2 hosting compatibility.

## Spatial and nonspatial execution

World-bound and mapless spatial expeditions use authoritative qualified cells and reciprocal interfaces from the selected periodic tiling. Existing hexagonal presets and supported square-based presets are configuration data, not separate movement engines. Finite map bounds are optional where the selected world/procedure supports them.

Nonspatial journeys have their own session context and continue to use pinned generic procedures. World truth, transient expedition state, player knowledge, and visibility remain separate authorities. Surveyor may analyze reference images, but an observed image pattern does not automatically replace the world tiling or authorize a source-map registration.

## Stack

- .NET 10 / ASP.NET Core
- PostgreSQL 18-compatible persistence through Npgsql and hand-written SQL
- TypeScript 7 / Vite 8
- Canvas 2D
- Filesystem-backed raster/source-map assets
- Docker
- xUnit and Node test runners

## Local development

Hex Crawl requires PostgreSQL for structured runtime persistence. Copy `.env.example` values into your local environment and change the development password as appropriate.

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

See `docs/postgresql-persistence.md`, `docs/modular-architecture.md`, `docs/design-references.md`, and `docs/tool-hosting.md` for persistence, module organization, design references, and hosting. `docs/architecture.md` preserves earlier hex-only architecture and phase decisions; its old schema and runtime descriptions are not the current implementation contract.

## Development roadmap

The transition from the original hex-only implementation is recorded in [the Tile Crawl development plan](docs/tile-crawl-development-plan.md). That plan includes dated historical checkpoints; use the current source and deployment records to determine operational status. The previous generic-procedure roadmap was retired, while implemented procedure and UX architecture remains documented separately.

# Dorks & Dice Hex Crawl

Initial foundation for the Dorks & Dice Hex Crawl tool.

This slice establishes a continuous-overworld domain model, mathematical hex geometry, semantic spatial features, runtime/player-knowledge separation, Embedded Module v2 hosting compatibility, and a Canvas-based interactive geometry demonstrator.

## Stack

- .NET 10 / ASP.NET Core
- TypeScript 7
- Vite 8
- Canvas 2D
- Docker
- xUnit
- Node test runner for frontend geometry/lifecycle tests

## Local development

```bash
cd src/HexCrawl.Web/Client
npm install
npm run build
cd ../../..
dotnet run --project src/HexCrawl.Web
```

Open the URL printed by ASP.NET Core. The demonstrator supports pan, zoom, selectable hexes, coordinate display, pointy/flat orientation, configurable physical scale, and semantic feature/location overlays.

## Validation

```bash
cd src/HexCrawl.Web/Client && npm run build
cd ../../..
dotnet test dorks-and-dice-hex-crawl.slnx -p:BuildClient=false
docker build -t dorks-and-dice-hex-crawl:test -f src/HexCrawl.Web/Dockerfile .
```

See `docs/architecture.md` and `docs/tool-hosting.md` for the decisions that this foundation establishes.

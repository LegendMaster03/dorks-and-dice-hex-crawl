from pathlib import Path

schema = Path("tests/HexCrawl.Application.Tests/PostgresSchemaTests.cs")
text = schema.read_text()
old = "        Assert.Equal(locations[937], loaded.World.Locations[937]);\n"
new = """        var expectedLocation = locations[937];
        var actualLocation = loaded.World.Locations[937];
        Assert.Equal(expectedLocation.Id, actualLocation.Id);
        Assert.Equal(expectedLocation.Name, actualLocation.Name);
        Assert.Equal(expectedLocation.Category, actualLocation.Category);
        Assert.Equal(expectedLocation.Position, actualLocation.Position);
        Assert.Equal(expectedLocation.Discoverability, actualLocation.Discoverability);
        Assert.Equal(expectedLocation.DetailMaps.ToArray(), actualLocation.DetailMaps.ToArray());
"""
if old not in text:
    raise SystemExit("Large snapshot assertion target not found")
schema.write_text(text.replace(old, new, 1))

migration = Path("tests/HexCrawl.Application.Tests/PostgresPersistenceMigrationTests.cs")
text = migration.read_text()
old = "            Assert.Equal(report, repeated);\n"
new = """            Assert.Equal(report.SourceSchemaVersion, repeated.SourceSchemaVersion);
            Assert.Equal(report.OverworldCount, repeated.OverworldCount);
            Assert.Equal(report.ExpeditionCount, repeated.ExpeditionCount);
            Assert.Equal(report.EventCount, repeated.EventCount);
            Assert.Equal(report.ContextKinds.ToArray(), repeated.ContextKinds.ToArray());
"""
if old not in text:
    raise SystemExit("Migration report assertion target not found")
migration.write_text(text.replace(old, new, 1))

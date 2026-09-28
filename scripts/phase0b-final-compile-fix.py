from pathlib import Path

path = Path("tests/HexCrawl.Application.Tests/PostgresPersistenceMigrationTests.cs")
text = path.read_text()
for line in (
    "            worldBound = worldBound!;\n",
    "                abstractHex = abstractHex!;\n",
    "                nonSpatial = nonSpatial!;\n",
):
    if line not in text:
        raise SystemExit(f"Expected line not found: {line.strip()}")
    text = text.replace(line, "", 1)
path.write_text(text)

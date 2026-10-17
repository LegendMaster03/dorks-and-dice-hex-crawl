# Source-raster response regressions

These are v3 wire responses generated from the exact D02 acceptance PNGs preserved in Surveyor's `test/fixtures/acceptance` corpus.

- `qa-regular-hex-grid-before.json` and `fallout-warfare2-9x9-blank-before.json` preserve deployed Surveyor 42a46ae output. The original Tile Crawl code failed the distant-period test on the former and rejected the 1584² raster envelope on the latter.
- The five unsuffixed responses capture the corrected generic detector. They include source projection, metric, and topology evidence rather than hand-authored idealized polygons.

Surveyor's `research/d02-acceptance-corpus.mjs` can reproduce/export responses. The integration test runs the actual HTTP client parser and final placement conversion, then compares all returned transforms to independent known printed vertices in first/middle/last rows and columns (2 source pixels maximum), as well as distant lattice repetition. Replacing responses does not change that oracle.

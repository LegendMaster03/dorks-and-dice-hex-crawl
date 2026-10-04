import fs from "node:fs";
import { pathToFileURL } from "node:url";

const legacyModulePath = process.env.PHASE13_LEGACY_DETECTOR;
const outputDirectory = process.env.PHASE13_FIXTURE_DIR;
if (!legacyModulePath || !outputDirectory) {
    throw new Error("PHASE13_LEGACY_DETECTOR and PHASE13_FIXTURE_DIR are required.");
}

const { detectHexLattice } = await import(pathToFileURL(legacyModulePath).href);
const radians = degrees => degrees * Math.PI / 180;
const raster = (width, height, value = 224) => ({
    width,
    height,
    pixels: new Uint8Array(width * height).fill(value)
});

function darkenPixel(image, x, y, value = 32) {
    const ix = Math.round(x);
    const iy = Math.round(y);
    if (ix < 0 || iy < 0 || ix >= image.width || iy >= image.height) return;
    const index = iy * image.width + ix;
    image.pixels[index] = Math.min(image.pixels[index], value);
}

function drawLine(image, start, end, value = 32, width = 1) {
    const steps = Math.max(1, Math.ceil(Math.hypot(end.x - start.x, end.y - start.y) * 1.5));
    for (let step = 0; step <= steps; step++) {
        const t = step / steps;
        const x = start.x + ((end.x - start.x) * t);
        const y = start.y + ((end.y - start.y) * t);
        for (let dy = -width; dy <= width; dy++) {
            for (let dx = -width; dx <= width; dx++) {
                if ((dx * dx) + (dy * dy) <= width * width + 0.5) {
                    darkenPixel(image, x + dx, y + dy, value);
                }
            }
        }
    }
}

function renderHexGrid(image, {
    orientation,
    spacing,
    rotationDegrees = 0,
    anchor = { x: 0, y: 0 },
    lineValue = 35
}) {
    const base = radians((orientation === "PointyTop" ? 0 : 30) + rotationDegrees);
    const u = { x: spacing * Math.cos(base), y: spacing * Math.sin(base) };
    const v = { x: spacing * Math.cos(base + Math.PI / 3), y: spacing * Math.sin(base + Math.PI / 3) };
    const radius = spacing / Math.sqrt(3);
    const cornerStart = (orientation === "PointyTop" ? -30 : 0) + rotationDegrees;
    const reach = Math.ceil(Math.hypot(image.width, image.height) / spacing) + 4;

    for (let i = -reach; i <= reach; i++) {
        for (let j = -reach; j <= reach; j++) {
            const center = {
                x: anchor.x + (i * u.x) + (j * v.x),
                y: anchor.y + (i * u.y) + (j * v.y)
            };
            if (center.x < -spacing || center.y < -spacing
                || center.x > image.width + spacing || center.y > image.height + spacing) continue;
            const corners = [];
            for (let corner = 0; corner < 6; corner++) {
                const angle = radians(cornerStart + corner * 60);
                corners.push({
                    x: center.x + radius * Math.cos(angle),
                    y: center.y + radius * Math.sin(angle)
                });
            }
            for (let edge = 0; edge < 6; edge++) {
                drawLine(image, corners[edge], corners[(edge + 1) % 6], lineValue, 1);
            }
        }
    }
}

function addNoise(image) {
    for (let y = 0; y < image.height; y++) {
        for (let x = 0; x < image.width; x++) {
            const index = y * image.width + x;
            const variation = ((x * 17 + y * 29 + ((x * y) % 31)) % 29) - 14;
            image.pixels[index] = Math.max(0, Math.min(255, image.pixels[index] + variation));
        }
    }
}

function replaceRightHalf(target, source) {
    const split = Math.floor(target.width / 2);
    for (let y = 0; y < target.height; y++) {
        for (let x = split; x < target.width; x++) {
            target.pixels[y * target.width + x] = source.pixels[y * source.width + x];
        }
    }
}

function writeRaw(image, name) {
    fs.writeFileSync(`${outputDirectory}/${name}.raw`, Buffer.from(image.pixels));
    fs.writeFileSync(`${outputDirectory}/${name}.dimensions.json`, JSON.stringify({
        width: image.width,
        height: image.height
    }));
}

const detected = raster(640, 480, 224);
renderHexGrid(detected, {
    orientation: "FlatTop",
    spacing: 30.001,
    anchor: { x: 9.5, y: 11.25 },
    lineValue: 48
});
const legacy = detectHexLattice(detected, {
    minimumSpacingPixels: 12,
    minimumConfidence: 0.54
});
if (legacy.status !== "detected" || !legacy.fit) {
    throw new Error(`Legacy fixture did not detect: ${legacy.reason}`);
}
fs.writeFileSync(`${outputDirectory}/legacy-analysis.json`, JSON.stringify(legacy, null, 2));
writeRaw(detected, "detected");
writeRaw(raster(320, 240, 224), "gridless");

const left = raster(640, 420, 220);
const right = raster(640, 420, 220);
renderHexGrid(left, { orientation: "FlatTop", spacing: 30, anchor: { x: 8, y: 17 }, lineValue: 64 });
renderHexGrid(right, { orientation: "FlatTop", spacing: 31, anchor: { x: 8, y: 17 }, lineValue: 64 });
addNoise(left);
addNoise(right);
replaceRightHalf(left, right);
writeRaw(left, "inconclusive");

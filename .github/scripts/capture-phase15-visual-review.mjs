import { spawn } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import process from "node:process";

const [chromeBinary, baseUrl, outputDir, procedureId, worldId, expeditionId, journeyId] = process.argv.slice(2);
if (![chromeBinary, baseUrl, outputDir, procedureId, worldId, expeditionId, journeyId].every(Boolean)) {
    throw new Error("Usage: capture-phase15-visual-review.mjs <chrome> <baseUrl> <outputDir> <procedureId> <worldId> <expeditionId> <journeyId>");
}

await mkdir(outputDir, { recursive: true });
const profile = `${outputDir}/chrome-profile`;
const chrome = spawn(chromeBinary, [
    "--headless=new",
    "--no-sandbox",
    "--disable-dev-shm-usage",
    "--remote-debugging-port=9222",
    `--user-data-dir=${profile}`,
    "about:blank"
], { stdio: ["ignore", "pipe", "pipe"] });

let stderr = "";
chrome.stderr.on("data", chunk => { stderr += chunk.toString(); });

try {
    const debuggerUrl = await waitForDebugger();
    const client = await CdpClient.connect(debuggerUrl);
    await client.send("Page.enable");
    await client.send("Runtime.enable");

    const metrics = [];
    const capture = async ({ name, url, width, height, setup, ready = ".hc-page", captureBottom = true }) => {
        await client.send("Emulation.setDeviceMetricsOverride", {
            width,
            height,
            deviceScaleFactor: 1,
            mobile: width <= 480
        });
        await client.send("Page.navigate", { url });
        await waitForReady(client, ready);
        if (setup) {
            await client.evaluate(setup);
            await sleep(350);
            await waitForReady(client, ready);
        }
        await sleep(250);
        await client.evaluate("window.scrollTo(0, 0)");
        await sleep(100);
        const layout = await client.evaluate(`(() => ({
            url: location.href,
            title: document.title,
            width: innerWidth,
            height: innerHeight,
            scrollWidth: document.documentElement.scrollWidth,
            scrollHeight: document.documentElement.scrollHeight,
            horizontalOverflow: document.documentElement.scrollWidth > innerWidth + 1,
            activeElement: document.activeElement?.tagName ?? null
        }))()`);
        metrics.push({ name, ...layout });
        await screenshot(client, `${outputDir}/${name}-top.png`);
        if (captureBottom && layout.scrollHeight > height + 20) {
            await client.evaluate("window.scrollTo(0, document.documentElement.scrollHeight)");
            await sleep(150);
            await screenshot(client, `${outputDir}/${name}-bottom.png`);
        }
    };

    const procedureUrl = `${baseUrl}/procedures/${procedureId}`;
    const spatialUrl = `${baseUrl}/worlds/${worldId}/expeditions/${expeditionId}`;
    const journeyUrl = `${baseUrl}/expeditions/${journeyId}`;

    await capture({
        name: "preset-browser-wide",
        url: `${baseUrl}/procedures`,
        width: 1440,
        height: 1000,
        setup: `localStorage.setItem("hex-crawl.procedure-mode", "compact"); location.reload();`,
        ready: ".hc-preset-browser"
    });
    await capture({
        name: "preset-browser-mobile",
        url: `${baseUrl}/procedures`,
        width: 390,
        height: 844,
        ready: ".hc-preset-browser"
    });

    await capture({
        name: "procedure-compact-laptop",
        url: procedureUrl,
        width: 1280,
        height: 800,
        setup: `localStorage.setItem("hex-crawl.procedure-mode", "compact"); location.reload();`,
        ready: ".hc-compact-procedure"
    });
    await capture({
        name: "procedure-advanced-laptop",
        url: procedureUrl,
        width: 1280,
        height: 800,
        setup: `localStorage.setItem("hex-crawl.procedure-mode", "advanced"); location.reload();`,
        ready: ".hc-advanced-procedure"
    });
    await capture({
        name: "procedure-json-laptop",
        url: procedureUrl,
        width: 1280,
        height: 800,
        setup: `localStorage.setItem("hex-crawl.procedure-mode", "json"); location.reload();`,
        ready: "textarea[aria-label=\"Canonical campaign procedure JSON\"]"
    });
    await capture({
        name: "procedure-json-invalid",
        url: procedureUrl,
        width: 1280,
        height: 800,
        ready: "textarea[aria-label=\"Canonical campaign procedure JSON\"]",
        setup: `(() => {
            const textarea = document.querySelector('textarea[aria-label="Canonical campaign procedure JSON"]');
            textarea.value = '{';
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
            [...document.querySelectorAll('button')].find(button => button.textContent?.trim() === 'Validate')?.click();
        })()`
    });

    for (const [name, width, height] of [
        ["spatial-wide", 1440, 900],
        ["spatial-embedded", 900, 800],
        ["spatial-tablet", 768, 1024],
        ["spatial-mobile", 390, 844]
    ]) {
        await capture({ name, url: spatialUrl, width, height, ready: "[data-phase15-runtime]" });
    }
    await capture({
        name: "spatial-party-drawer",
        url: spatialUrl,
        width: 900,
        height: 800,
        ready: "[data-phase15-runtime]",
        setup: `[...document.querySelectorAll('button')].find(button => button.textContent?.trim() === 'Party')?.click();`
    });

    await capture({
        name: "journey-wide",
        url: journeyUrl,
        width: 1280,
        height: 800,
        ready: "[data-phase15-runtime]"
    });
    await capture({
        name: "journey-mobile",
        url: journeyUrl,
        width: 390,
        height: 844,
        ready: "[data-phase15-runtime]"
    });

    await writeFile(`${outputDir}/layout-metrics.json`, JSON.stringify(metrics, null, 2));
    const overflows = metrics.filter(value => value.horizontalOverflow);
    if (overflows.length > 0) {
        throw new Error(`Horizontal overflow detected: ${overflows.map(value => value.name).join(", ")}`);
    }
    client.close();
} finally {
    chrome.kill("SIGTERM");
    await Promise.race([
        new Promise(resolve => chrome.once("exit", resolve)),
        sleep(2000)
    ]);
    if (chrome.exitCode && chrome.exitCode !== 0) {
        console.error(stderr);
    }
}

async function waitForDebugger() {
    for (let attempt = 0; attempt < 80; attempt += 1) {
        try {
            const targets = await fetch("http://127.0.0.1:9222/json").then(response => response.json());
            const target = targets.find(value => value.type === "page" && value.webSocketDebuggerUrl);
            if (target) return target.webSocketDebuggerUrl;
        } catch {
            // Chrome is still starting.
        }
        await sleep(100);
    }
    throw new Error("Chrome DevTools endpoint did not become ready.");
}

async function waitForReady(client, selector) {
    for (let attempt = 0; attempt < 100; attempt += 1) {
        const ready = await client.evaluate(`document.readyState === "complete" && Boolean(document.querySelector(${JSON.stringify(selector)}))`);
        if (ready) return;
        await sleep(100);
    }
    const body = await client.evaluate("document.body?.innerText?.slice(0, 2000) ?? ''");
    throw new Error(`Timed out waiting for ${selector}. Page text: ${body}`);
}

async function screenshot(client, path) {
    const result = await client.send("Page.captureScreenshot", {
        format: "png",
        fromSurface: true,
        captureBeyondViewport: false
    });
    await writeFile(path, Buffer.from(result.data, "base64"));
}

function sleep(milliseconds) {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
}

class CdpClient {
    static async connect(url) {
        const socket = new WebSocket(url);
        await new Promise((resolve, reject) => {
            socket.addEventListener("open", resolve, { once: true });
            socket.addEventListener("error", reject, { once: true });
        });
        return new CdpClient(socket);
    }

    constructor(socket) {
        this.socket = socket;
        this.nextId = 1;
        this.pending = new Map();
        socket.addEventListener("message", event => {
            const message = JSON.parse(event.data);
            if (!message.id) return;
            const pending = this.pending.get(message.id);
            if (!pending) return;
            this.pending.delete(message.id);
            if (message.error) pending.reject(new Error(`${message.error.code}: ${message.error.message}`));
            else pending.resolve(message.result ?? {});
        });
    }

    send(method, params = {}) {
        const id = this.nextId++;
        return new Promise((resolve, reject) => {
            this.pending.set(id, { resolve, reject });
            this.socket.send(JSON.stringify({ id, method, params }));
        });
    }

    async evaluate(expression) {
        const result = await this.send("Runtime.evaluate", {
            expression,
            awaitPromise: true,
            returnByValue: true
        });
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.text ?? "Runtime evaluation failed.");
        return result.result?.value;
    }

    close() {
        this.socket.close();
    }
}

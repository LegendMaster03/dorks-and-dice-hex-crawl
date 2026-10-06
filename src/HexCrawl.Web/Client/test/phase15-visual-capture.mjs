import { chromium } from "playwright";

const base = process.env.PHASE15_VISUAL_URL ?? "http://127.0.0.1:4173/test/phase15-visual-harness.html";
const scenarios = [
    ["home-light","home","light"],
    ["catalog-light","catalog","light"],
    ["inspect-alex","inspect-alex","light"],
    ["inspect-bx","inspect-bx","light"],
    ["inspect-survival","inspect-survival","light"],
    ["inspect-journey","inspect-journey","light"],
    ["blank-compact","blank","light"],
    ["simple-compact","simple","light"],
    ["complex-compact","complex","light"],
    ["journey-compact","journey","light"],
    ["focus-compact","focus","light"],
    ["advanced-empty","advanced-empty","light"],
    ["advanced-complex","advanced-complex","light"],
    ["json-light","json","light"],
    ["complex-dark","complex","dark"],
    ["catalog-dark","catalog","dark"]
];

const browser = await chromium.launch({headless:true});
try {
    for (const [name,scenario,theme] of scenarios) {
        const page = await browser.newPage({viewport:{width:1440,height:1000},deviceScaleFactor:1});
        await page.goto(`${base}?scenario=${encodeURIComponent(scenario)}&theme=${theme}`, {waitUntil:"networkidle"});
        await page.waitForSelector("body[data-visual-ready='true']");
        await page.waitForTimeout(80);

        const metrics = await page.evaluate(() => {
            const root = document.querySelector("#tool-root");
            const style = getComputedStyle(root);
            return {
                text: root?.textContent ?? "",
                rootWidth: root?.scrollWidth ?? 0,
                rootClientWidth: root?.clientWidth ?? 0,
                pageWidth: document.documentElement.scrollWidth,
                pageClientWidth: document.documentElement.clientWidth,
                surface: style.getPropertyValue("--hc-surface").trim(),
                textColor: style.getPropertyValue("--hc-text").trim(),
                focus: style.getPropertyValue("--hc-focus").trim(),
                saveDisabled: document.querySelector("[data-procedure-save]")?.disabled ?? null
            };
        });

        if (metrics.pageWidth > metrics.pageClientWidth + 2) {
            throw new Error(`${name}: horizontal page overflow ${metrics.pageWidth} > ${metrics.pageClientWidth}`);
        }
        if (metrics.rootWidth > metrics.rootClientWidth + 2) {
            throw new Error(`${name}: root overflow ${metrics.rootWidth} > ${metrics.rootClientWidth}`);
        }
        if (theme === "dark" && metrics.surface !== "#151824") throw new Error(`${name}: dark surface token not applied (${metrics.surface})`);
        if (theme === "light" && metrics.surface !== "#ffffff") throw new Error(`${name}: light surface token not applied (${metrics.surface})`);

        if (scenario.startsWith("inspect-") || ["blank","simple","complex","journey","focus"].includes(scenario)) {
            for (const banned of ["144000000000","Duration Ticks","Procedure support","Resolution helpers","Yes · Yes"]) {
                if (metrics.text.includes(banned)) throw new Error(`${name}: user-facing raw/internal value '${banned}'`);
            }
        }
        if (scenario.startsWith("inspect-")) {
            for (const banned of ["time.interval","movement.resolution","navigation.outcome","procedure.helpers"]) {
                if (metrics.text.includes(banned)) throw new Error(`${name}: Inspect leaked technical identifier '${banned}'`);
            }
        }
        if (scenario === "blank") {
            if (!metrics.text.includes("Add exploration rules")) throw new Error("blank Compact did not expose rule library");
            if (!metrics.text.includes("Navigation")) throw new Error("blank Compact did not expose navigation");
        }
        if (scenario === "complex") {
            for (const expected of ["At the table","Travel activities","Getting lost and recovery","Encounter schedule","Environmental exposure","Persistent effects","Automatic resolution"]) {
                if (!metrics.text.includes(expected)) throw new Error(`complex Compact missing ${expected}`);
            }
        }
        if (scenario === "journey") {
            if (!metrics.text.includes("Journey & events") || !metrics.text.includes("Multi-stage journey")) {
                throw new Error("journey-first Compact did not present journey rules");
            }
        }
        if (scenario === "focus" && !(await page.locator(".hc-focus-workspace").count())) {
            throw new Error("Compact focused editor did not open");
        }
        if (scenario.startsWith("advanced")) {
            const nav = page.locator(".hc-advanced-module-select");
            if (await nav.count() < 2) throw new Error(`${name}: Advanced structure rail did not render`);
            await nav.first().focus();
            const before = await page.evaluate(() => document.activeElement?.getAttribute("data-module-key"));
            await page.keyboard.press("ArrowDown");
            const after = await page.evaluate(() => document.activeElement?.getAttribute("data-module-key"));
            if (!after || before === after) throw new Error(`${name}: Advanced arrow-key navigation did not move focus`);
            await nav.nth(1).hover();
        }
        if (scenario === "advanced-complex") {
            for (const expected of ["Mechanic","Automation","Execution handler","Parameters","Inputs","Produces","Required dependencies","Optional dependencies","Diagnostics"]) {
                if (!metrics.text.includes(expected)) throw new Error(`Advanced inspector missing ${expected}`);
            }
        }
        if (scenario === "json" && !metrics.text.includes("Canonical JSON")) throw new Error("JSON editor did not render");
        if (["simple","complex","journey","advanced-complex","json"].includes(scenario) && metrics.saveDisabled !== true) {
            throw new Error(`${name}: unchanged saved procedure should have disabled Save`);
        }

        const image = await page.screenshot({type:"jpeg",quality:68,fullPage:true});
        process.stdout.write(`BEGIN_IMAGE:${name}\n${image.toString("base64")}\nEND_IMAGE:${name}\n`);
        await page.close();
    }
    console.log("PHASE15_VISUAL_ASSERTIONS_OK");
} finally {
    await browser.close();
}

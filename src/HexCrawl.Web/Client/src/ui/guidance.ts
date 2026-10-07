const GUIDED_STORAGE_KEY = "hex-crawl.guided.enabled";

export function guidedExperienceEnabled(): boolean {
    try {
        const stored = localStorage.getItem(GUIDED_STORAGE_KEY);
        return stored !== "false";
    } catch {
        return true;
    }
}

export function setGuidedExperienceEnabled(enabled: boolean): void {
    try {
        localStorage.setItem(GUIDED_STORAGE_KEY, enabled ? "true" : "false");
    } catch {
        // Guidance remains usable even when browser storage is unavailable.
    }
}

export function applyGuidedExperience(root: HTMLElement): void {
    root.classList.toggle("hc-guidance-off", !guidedExperienceEnabled());
}

export function guidancePreferenceButton(root: HTMLElement): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "hc-guidance-toggle";

    const sync = (): void => {
        const enabled = guidedExperienceEnabled();
        button.textContent = enabled ? "Hide beginner help" : "Show beginner help";
        button.setAttribute("aria-pressed", String(enabled));
        button.setAttribute("aria-label", enabled ? "Hide Guided beginner help" : "Show Guided beginner help");
    };

    button.addEventListener("click", () => {
        setGuidedExperienceEnabled(!guidedExperienceEnabled());
        applyGuidedExperience(root);
        sync();
    });
    sync();
    return button;
}

export function attachFieldHelp(field: HTMLElement, title: string, explanation: string, example?: string): void {
    const label = field.closest("label");
    if (!label) return;
    const help = guidedDisclosure("What does this mean?", title, explanation, example);
    help.classList.add("hc-guided-help");
    help.dataset.helpFor = field.getAttribute("name") ?? title;
    label.insertAdjacentElement("afterend", help);
}

export function guidedDisclosure(summaryText: string, title: string, explanation: string, example?: string): HTMLDetailsElement {
    const details = document.createElement("details");
    details.className = "hc-guided-help hc-guided-only";
    details.dataset.guidedHelp = title;

    const summary = document.createElement("summary");
    summary.textContent = summaryText;
    summary.setAttribute("aria-label", `${summaryText} ${title}`);

    const body = document.createElement("div");
    body.className = "hc-guided-help-body";
    const heading = document.createElement("strong");
    heading.textContent = title;
    const copy = document.createElement("p");
    copy.textContent = explanation;
    body.append(heading, copy);

    if (example) {
        const sample = document.createElement("p");
        sample.className = "hc-guided-help-example";
        sample.textContent = `Example: ${example}`;
        body.append(sample);
    }

    details.append(summary, body);
    return details;
}

export function guidedCallout(title: string, bodyText: string, steps: string[] = []): HTMLElement {
    const section = document.createElement("section");
    section.className = "hc-guided-callout hc-guided-only";
    const heading = document.createElement("h3");
    heading.textContent = title;
    const body = document.createElement("p");
    body.textContent = bodyText;
    section.append(heading, body);
    if (steps.length > 0) {
        const list = document.createElement("ol");
        list.className = "hc-guided-steps";
        for (const step of steps) {
            const item = document.createElement("li");
            item.textContent = step;
            list.append(item);
        }
        section.append(list);
    }
    return section;
}

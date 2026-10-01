import type { HexCrawlApi } from "../../api";
import { formatDistance } from "../../runtime-view";
import type {
    DistanceUnit,
    DistanceValue,
    ExpeditionDetail,
    ExpeditionParty,
    ParticipantActivityAssignment,
    ParticipantActivityPolicy,
    ParticipantActivityAssignmentScope,
    PartyMovementReference
} from "../../types";
import { required } from "../../ui/dom";
import { movementContributorsAfterMemberRemoval } from "./expedition-party-movement";

type PartyMutationRunner = (
    control: HTMLButtonElement | null,
    action: () => Promise<void>) => Promise<void>;

type MovementKey = "perHour" | "perWatch" | "perMarch";

export class ExpeditionPartySheetController {
    private draft: ExpeditionParty | null = null;
    private latestVersion = 0;
    private dirty = false;
    private runtimeForUnits: ExpeditionDetail | null = null;

    public constructor(
        private readonly root: HTMLElement,
        private readonly api: HexCrawlApi,
        private readonly getRuntime: () => ExpeditionDetail,
        private readonly applyRuntime: (runtime: ExpeditionDetail) => void,
        private readonly mutate: PartyMutationRunner) {}

    public sync(runtime: ExpeditionDetail): void {
        this.latestVersion = runtime.version;
        this.runtimeForUnits = runtime;
        this.renderSummary(runtime);
        if (!this.dirty || this.draft === null) {
            this.draft = cloneParty(runtime.party);
            this.dirty = false;
            this.renderEditor();
        } else {
            this.renderEditorState();
        }
    }

    private renderSummary(runtime: ExpeditionDetail): void {
        const host = required<HTMLElement>(this.root, "[data-party-summary]");
        const party = runtime.party;
        if (party.members.length === 0
            && party.marchingOrder.length === 0
            && party.watchList.length === 0
            && party.standingOrders.length === 0
            && party.activityAssignments.length === 0
            && party.baseMovement === null) {
            host.innerHTML = `
                <p class="hc-sheet-empty">
                    Party sheet is not configured yet. Add party members, marching order, watches,
                    standing orders, participant assignments, and movement references from the Party & travel order panel.
                </p>`;
            return;
        }

        const memberById = new Map(party.members.map(member => [member.id, member.name]));
        const marching = [...party.marchingOrder]
            .sort((left, right) => left.rank - right.rank || left.file - right.file)
            .map(position => `R${position.rank} F${position.file}: ${memberById.get(position.memberId) ?? "Unknown"}`)
            .join(" · ") || "—";
        const watches = [...party.watchList]
            .sort((left, right) => left.slot - right.slot)
            .map(entry => {
                const names = entry.memberIds.map(id => memberById.get(id) ?? "Unknown").join(", ") || "unassigned";
                return `${entry.label?.trim() || `Watch ${entry.slot}`}: ${names}`;
            })
            .join(" · ") || "—";
        const movement = movementSummary(party.baseMovement, memberById);
        const orders = party.standingOrders
            .filter(order => order.enabled)
            .map(order => order.text)
            .join(" · ") || "—";
        const assignments = party.activityAssignments
            .map(assignment => assignmentSummary(assignment, memberById))
            .join(" · ") || "—";

        host.innerHTML = `
            <div class="hc-party-register-grid">
                <div><strong>Party</strong><span>${escapeHtml(party.members.map(member => member.name).join(", ") || "—")}</span></div>
                <div><strong>Movement</strong><span>${escapeHtml(movement)}</span></div>
                <div class="hc-party-register-wide"><strong>Participant roles / activities</strong><span>${escapeHtml(assignments)}</span></div>
                <div class="hc-party-register-wide"><strong>Marching order</strong><span>${escapeHtml(marching)}</span></div>
                <div class="hc-party-register-wide"><strong>Watch list</strong><span>${escapeHtml(watches)}</span></div>
                <div class="hc-party-register-wide"><strong>Standing orders</strong><span>${escapeHtml(orders)}</span></div>
            </div>`;
    }

    private renderEditor(): void {
        const host = required<HTMLElement>(this.root, "[data-party-editor]");
        const party = this.requireDraft();
        const memberOptions = party.members
            .map(member => `<option value="${member.id}">${escapeHtml(member.name)}</option>`)
            .join("");

        host.innerHTML = `
            <form class="hc-form hc-party-form" data-party-form>
                <p class="hc-hint">
                    Persistent table-facing information. Participant roles and activities are expedition state;
                    the selected campaign procedure defines which generic assignments are available.
                </p>

                <fieldset>
                    <legend>Party</legend>
                    <div class="hc-party-editor-list" data-party-members></div>
                    <button type="button" data-party-add-member>Add party member</button>
                </fieldset>

                <fieldset>
                    <legend>Participant roles / activities</legend>
                    <div data-activity-policy></div>
                    <div class="hc-party-editor-list" data-activity-assignments></div>
                    <button type="button" data-party-add-assignment>Add assignment</button>
                </fieldset>

                <fieldset>
                    <legend>Marching order</legend>
                    <p class="hc-hint">Rank and file are deliberately open-ended; the sheet does not assume a fixed formation size.</p>
                    <div class="hc-party-editor-list" data-marching-order></div>
                    <button type="button" data-party-add-position>Add marching position</button>
                </fieldset>

                <fieldset>
                    <legend>Watch list</legend>
                    <p class="hc-hint">Use as many watch slots as the campaign needs. Members can appear in multiple watch slots.</p>
                    <div class="hc-party-editor-list" data-watch-list></div>
                    <button type="button" data-party-add-watch>Add watch slot</button>
                </fieldset>

                <fieldset>
                    <legend>Standing orders</legend>
                    <div class="hc-party-editor-list" data-standing-orders></div>
                    <button type="button" data-party-add-order>Add standing order</button>
                </fieldset>

                <fieldset>
                    <legend>Movement reference</legend>
                    <p class="hc-hint">Hour / Watch / March values are optional reference rates. No unit or distance is assumed.</p>
                    <div class="hc-party-movement-grid">
                        ${this.movementFieldMarkup("perHour", "Per hour")}
                        ${this.movementFieldMarkup("perWatch", "Per watch")}
                        ${this.movementFieldMarkup("perMarch", "Per march")}
                    </div>
                    <label>
                        Limiting party member
                        <select name="limitingMember">
                            <option value="">—</option>
                            ${memberOptions}
                        </select>
                    </label>
                    <label>
                        Movement note
                        <textarea name="movementNote" rows="2" maxlength="1000" placeholder="mounts, load, route assumptions, or other persistent context"></textarea>
                    </label>
                </fieldset>

                <div class="hc-party-save-row">
                    <span class="hc-hint" data-party-save-state>Saved party sheet</span>
                    <div class="hc-button-row">
                        <button type="button" data-party-reset disabled>Discard changes</button>
                        <button type="submit" class="hc-primary-action" data-party-save disabled>Save party sheet</button>
                    </div>
                </div>
            </form>`;

        this.renderMembers();
        this.renderActivityAssignments();
        this.renderMarchingOrder();
        this.renderWatchList();
        this.renderStandingOrders();
        this.bindStaticEditorControls();
        this.populateMovementControls();
        this.renderEditorState();
    }

    private renderMembers(): void {
        const host = required<HTMLElement>(this.root, "[data-party-members]");
        const party = this.requireDraft();
        host.replaceChildren();

        if (party.members.length === 0) {
            host.append(emptyLine("No party members recorded."));
            return;
        }

        for (const member of party.members) {
            const row = document.createElement("div");
            row.className = "hc-party-editor-row hc-party-member-row";
            row.innerHTML = `
                <label>Name <input data-member-name required maxlength="200"></label>
                <label>Character link / id <input data-member-external maxlength="512" placeholder="optional"></label>
                <label class="hc-party-checkbox"><input data-member-movement type="checkbox"> Counts toward party movement</label>
                <button type="button" class="hc-danger-action" data-remove-member>Remove</button>`;
            const name = required<HTMLInputElement>(row, "[data-member-name]");
            name.value = member.name;
            name.addEventListener("input", () => {
                member.name = name.value;
                this.markDirty();
            });
            const external = required<HTMLInputElement>(row, "[data-member-external]");
            external.value = member.externalCharacterId ?? "";
            external.addEventListener("input", () => {
                member.externalCharacterId = external.value.trim() || null;
                this.markDirty();
            });
            const counts = required<HTMLInputElement>(row, "[data-member-movement]");
            counts.checked = member.countsTowardPartyMovement;
            counts.addEventListener("change", () => {
                member.countsTowardPartyMovement = counts.checked;
                this.markDirty();
            });
            required<HTMLButtonElement>(row, "[data-remove-member]").addEventListener("click", () => {
                this.removeMember(member.id);
            });
            host.append(row);
        }
    }

    private renderActivityAssignments(): void {
        const policyHost = required<HTMLElement>(this.root, "[data-activity-policy]");
        const host = required<HTMLElement>(this.root, "[data-activity-assignments]");
        const add = required<HTMLButtonElement>(this.root, "[data-party-add-assignment]");
        const party = this.requireDraft();
        const policy = this.requirePolicy();
        host.replaceChildren();

        if (policy.support === "None") {
            policyHost.innerHTML = `<p class="hc-hint">The current procedure does not define participant activity assignments.</p>`;
            add.hidden = true;
            renderReadOnlyAssignments(host, party.activityAssignments, party);
            return;
        }
        if (policy.support === "Unsupported") {
            policyHost.innerHTML = `<p class="hc-hint">This procedure contains a structural or future activity policy that this editor does not understand. Existing typed assignment state is preserved.</p>`;
            add.hidden = true;
            renderReadOnlyAssignments(host, party.activityAssignments, party);
            return;
        }

        const scope = policy.assignmentScope!;
        policyHost.innerHTML = `
            <p class="hc-hint">
                ${escapeHtml(humanizeKey(scope))} assignments · ${escapeHtml(humanizeKey(policy.activityBudgetModel ?? "unspecified-budget"))}.
                Available activities: ${escapeHtml(policy.activityKeys.map(humanizeKey).join(", ") || "none")}.
                Available roles: ${escapeHtml(policy.roleKeys.map(humanizeKey).join(", ") || "none")}.
            </p>`;
        add.hidden = false;
        add.disabled = (scope !== "Party" && party.members.length === 0)
            || (scope !== "Role" && policy.activityKeys.length === 0)
            || (scope === "Role" && policy.roleKeys.length === 0);

        if (party.activityAssignments.length === 0) {
            host.append(emptyLine("No participant roles or activities are currently assigned."));
            return;
        }

        for (const assignment of party.activityAssignments) {
            const row = document.createElement("div");
            row.className = "hc-party-editor-row hc-activity-assignment-row";

            if (scope !== "Party") {
                const label = document.createElement("label");
                label.textContent = "Participant ";
                const select = document.createElement("select");
                select.required = true;
                for (const member of party.members) {
                    const option = document.createElement("option");
                    option.value = member.id;
                    option.textContent = member.name;
                    select.append(option);
                }
                select.value = assignment.participantId ?? "";
                select.addEventListener("change", () => {
                    assignment.participantId = select.value || null;
                    this.markDirty();
                });
                label.append(select);
                row.append(label);
            }

            if (scope !== "Role" || policy.activityKeys.length > 0) {
                row.append(this.assignmentKeyControl(
                    "Activity",
                    assignment.activityKey,
                    policy.activityKeys,
                    scope !== "Role",
                    value => {
                        assignment.activityKey = value;
                        this.markDirty();
                    }));
            }

            if (scope === "Role" || policy.roleKeys.length > 0) {
                row.append(this.assignmentKeyControl(
                    "Role",
                    assignment.roleKey,
                    policy.roleKeys,
                    scope === "Role",
                    value => {
                        assignment.roleKey = value;
                        this.markDirty();
                    }));
            }

            const noteLabel = document.createElement("label");
            noteLabel.textContent = "Context ";
            const note = document.createElement("input");
            note.maxLength = 1000;
            note.placeholder = "optional";
            note.value = assignment.note ?? "";
            note.addEventListener("input", () => {
                assignment.note = note.value.trim() || null;
                this.markDirty();
            });
            noteLabel.append(note);
            row.append(noteLabel);

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "hc-danger-action";
            remove.textContent = "Remove";
            remove.addEventListener("click", () => {
                party.activityAssignments = party.activityAssignments.filter(item => item.id !== assignment.id);
                this.markDirty();
                this.renderActivityAssignments();
            });
            row.append(remove);
            host.append(row);
        }
    }

    private assignmentKeyControl(
        labelText: string,
        current: string | null,
        keys: string[],
        requiredValue: boolean,
        write: (value: string | null) => void): HTMLLabelElement {
        const label = document.createElement("label");
        label.textContent = `${labelText} `;
        const select = document.createElement("select");
        select.required = requiredValue;
        if (!requiredValue) {
            const empty = document.createElement("option");
            empty.value = "";
            empty.textContent = "—";
            select.append(empty);
        }
        for (const key of keys) {
            const option = document.createElement("option");
            option.value = key;
            option.textContent = humanizeKey(key);
            select.append(option);
        }
        select.value = current ?? "";
        select.addEventListener("change", () => write(select.value || null));
        label.append(select);
        return label;
    }

    private renderMarchingOrder(): void {
        const host = required<HTMLElement>(this.root, "[data-marching-order]");
        const party = this.requireDraft();
        host.replaceChildren();

        if (party.marchingOrder.length === 0) {
            host.append(emptyLine("No marching order recorded."));
            return;
        }

        for (const position of party.marchingOrder) {
            const row = document.createElement("div");
            row.className = "hc-party-editor-row hc-marching-row";
            const unavailable = new Set(
                party.marchingOrder.filter(item => item !== position).map(item => item.memberId));
            const options = party.members
                .filter(member => member.id === position.memberId || !unavailable.has(member.id))
                .map(member => `<option value="${member.id}">${escapeHtml(member.name)}</option>`)
                .join("");
            row.innerHTML = `
                <label>Member <select data-position-member required>${options}</select></label>
                <label>Rank <input data-position-rank type="number" min="0" step="1" required></label>
                <label>File <input data-position-file type="number" min="0" step="1" required></label>
                <button type="button" class="hc-danger-action" data-remove-position>Remove</button>`;
            const member = required<HTMLSelectElement>(row, "[data-position-member]");
            member.value = position.memberId;
            member.addEventListener("change", () => {
                position.memberId = member.value;
                this.markDirty();
                this.renderMarchingOrder();
            });
            const rank = required<HTMLInputElement>(row, "[data-position-rank]");
            rank.value = String(position.rank);
            rank.addEventListener("input", () => {
                if (rank.value !== "" && rank.validity.valid) position.rank = Number(rank.value);
                this.markDirty();
            });
            const file = required<HTMLInputElement>(row, "[data-position-file]");
            file.value = String(position.file);
            file.addEventListener("input", () => {
                if (file.value !== "" && file.validity.valid) position.file = Number(file.value);
                this.markDirty();
            });
            required<HTMLButtonElement>(row, "[data-remove-position]").addEventListener("click", () => {
                party.marchingOrder = party.marchingOrder.filter(item => item !== position);
                this.markDirty();
                this.renderMarchingOrder();
            });
            host.append(row);
        }
    }

    private renderWatchList(): void {
        const host = required<HTMLElement>(this.root, "[data-watch-list]");
        const party = this.requireDraft();
        host.replaceChildren();

        if (party.watchList.length === 0) {
            host.append(emptyLine("No watch rotation recorded."));
            return;
        }

        for (const entry of party.watchList) {
            const row = document.createElement("div");
            row.className = "hc-watch-entry";
            row.innerHTML = `
                <div class="hc-party-editor-row hc-watch-heading-row">
                    <label>Slot <input data-watch-slot type="number" min="1" step="1" required></label>
                    <label>Label <input data-watch-label maxlength="200" placeholder="First watch, dawn, night 2..."></label>
                    <button type="button" class="hc-danger-action" data-remove-watch>Remove</button>
                </div>
                <div class="hc-watch-members" data-watch-members></div>`;
            const slot = required<HTMLInputElement>(row, "[data-watch-slot]");
            slot.value = String(entry.slot);
            slot.addEventListener("input", () => {
                if (slot.value !== "" && slot.validity.valid) entry.slot = Number(slot.value);
                this.markDirty();
            });
            const label = required<HTMLInputElement>(row, "[data-watch-label]");
            label.value = entry.label ?? "";
            label.addEventListener("input", () => {
                entry.label = label.value.trim() || null;
                this.markDirty();
            });
            required<HTMLButtonElement>(row, "[data-remove-watch]").addEventListener("click", () => {
                party.watchList = party.watchList.filter(item => item !== entry);
                this.markDirty();
                this.renderWatchList();
            });

            const memberHost = required<HTMLElement>(row, "[data-watch-members]");
            if (party.members.length === 0) {
                memberHost.append(emptyLine("Add party members before assigning a watch."));
            } else {
                for (const member of party.members) {
                    const option = document.createElement("label");
                    option.className = "hc-watch-member";
                    const checkbox = document.createElement("input");
                    checkbox.type = "checkbox";
                    checkbox.checked = entry.memberIds.includes(member.id);
                    checkbox.addEventListener("change", () => {
                        entry.memberIds = checkbox.checked
                            ? [...new Set([...entry.memberIds, member.id])]
                            : entry.memberIds.filter(id => id !== member.id);
                        this.markDirty();
                    });
                    option.append(checkbox, document.createTextNode(member.name));
                    memberHost.append(option);
                }
            }
            host.append(row);
        }
    }

    private renderStandingOrders(): void {
        const host = required<HTMLElement>(this.root, "[data-standing-orders]");
        const party = this.requireDraft();
        host.replaceChildren();

        if (party.standingOrders.length === 0) {
            host.append(emptyLine("No standing orders recorded."));
            return;
        }

        for (const order of party.standingOrders) {
            const row = document.createElement("div");
            row.className = "hc-party-editor-row hc-standing-order-row";
            row.innerHTML = `
                <label class="hc-party-checkbox"><input data-order-enabled type="checkbox"> Active</label>
                <label>Standing order <input data-order-text required maxlength="2000"></label>
                <button type="button" class="hc-danger-action" data-remove-order>Remove</button>`;
            const enabled = required<HTMLInputElement>(row, "[data-order-enabled]");
            enabled.checked = order.enabled;
            enabled.addEventListener("change", () => {
                order.enabled = enabled.checked;
                this.markDirty();
            });
            const text = required<HTMLInputElement>(row, "[data-order-text]");
            text.value = order.text;
            text.addEventListener("input", () => {
                order.text = text.value;
                this.markDirty();
            });
            required<HTMLButtonElement>(row, "[data-remove-order]").addEventListener("click", () => {
                party.standingOrders = party.standingOrders.filter(item => item !== order);
                this.markDirty();
                this.renderStandingOrders();
            });
            host.append(row);
        }
    }

    private bindStaticEditorControls(): void {
        const form = required<HTMLFormElement>(this.root, "[data-party-form]");
        const party = this.requireDraft();

        required<HTMLButtonElement>(form, "[data-party-add-member]").addEventListener("click", () => {
            party.members.push({
                id: crypto.randomUUID(),
                name: "New member",
                externalCharacterId: null,
                countsTowardPartyMovement: true
            });
            this.markDirty();
            this.renderEditor();
        });

        const addAssignment = required<HTMLButtonElement>(form, "[data-party-add-assignment]");
        addAssignment.addEventListener("click", () => {
            const policy = this.requirePolicy();
            if (policy.support !== "Supported" || policy.assignmentScope === null) return;
            const assignment = newAssignment(policy, party);
            if (!assignment) return;
            party.activityAssignments.push(assignment);
            this.markDirty();
            this.renderActivityAssignments();
        });

        const addPosition = required<HTMLButtonElement>(form, "[data-party-add-position]");
        const assigned = new Set(party.marchingOrder.map(item => item.memberId));
        addPosition.disabled = party.members.length === 0 || party.members.every(member => assigned.has(member.id));
        addPosition.addEventListener("click", () => {
            const member = party.members.find(item => !assigned.has(item.id));
            if (!member) return;
            const rank = party.marchingOrder.reduce((max, item) => Math.max(max, item.rank), -1) + 1;
            party.marchingOrder.push({ memberId: member.id, rank, file: 0 });
            this.markDirty();
            this.renderEditor();
        });

        required<HTMLButtonElement>(form, "[data-party-add-watch]").addEventListener("click", () => {
            const slot = party.watchList.reduce((max, item) => Math.max(max, item.slot), 0) + 1;
            party.watchList.push({ slot, memberIds: [], label: null });
            this.markDirty();
            this.renderEditor();
        });

        required<HTMLButtonElement>(form, "[data-party-add-order]").addEventListener("click", () => {
            party.standingOrders.push({ id: crypto.randomUUID(), text: "", enabled: true });
            this.markDirty();
            this.renderEditor();
        });

        const limitingMember = required<HTMLSelectElement>(form, 'select[name="limitingMember"]');
        limitingMember.value = party.baseMovement?.limitingMemberId ?? "";
        limitingMember.addEventListener("change", () => {
            const movement = this.ensureMovement();
            movement.limitingMemberId = limitingMember.value || null;
            this.compactMovement();
            this.markDirty();
        });

        const movementNote = required<HTMLTextAreaElement>(form, 'textarea[name="movementNote"]');
        movementNote.value = party.baseMovement?.note ?? "";
        movementNote.addEventListener("input", () => {
            const movement = this.ensureMovement();
            movement.note = movementNote.value.trim() || null;
            this.compactMovement();
            this.markDirty();
        });

        const reset = required<HTMLButtonElement>(form, "[data-party-reset]");
        reset.addEventListener("click", () => {
            this.draft = cloneParty(this.getRuntime().party);
            this.dirty = false;
            this.latestVersion = this.getRuntime().version;
            this.renderEditor();
        });

        const save = required<HTMLButtonElement>(form, "[data-party-save]");
        form.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(save, async () => {
                this.validateDraft();
                const draft = this.requireDraft();
                const request = {
                    expectedVersion: this.latestVersion,
                    ...cloneParty(draft, draft.movementContributors !== undefined)
                };
                const saved = await this.api.updateExpeditionParty(this.getRuntime().id, request);
                this.draft = cloneParty(saved.party);
                this.latestVersion = saved.version;
                this.dirty = false;
                this.applyRuntime(saved);
            });
        });
    }

    private movementFieldMarkup(key: MovementKey, label: string): string {
        const distance = this.requireDraft().baseMovement?.[key] ?? null;
        const unit = distance?.unit ?? this.preferredUnit();
        return `
            <div class="hc-movement-reference" data-movement-key="${key}">
                <label>${label} <input data-movement-value type="number" min="0" step="any" value="${distance?.value ?? ""}"></label>
                <label>Unit
                    <select data-movement-unit>
                        <option value="Mile"${unit.kind === "Mile" ? " selected" : ""}>Miles</option>
                        <option value="Kilometer"${unit.kind === "Kilometer" ? " selected" : ""}>Kilometers</option>
                        <option value="Custom"${unit.kind === "Custom" ? " selected" : ""}>Custom</option>
                    </select>
                </label>
                <label data-custom-symbol>Symbol <input data-movement-symbol maxlength="24" value="${escapeHtml(unit.kind === "Custom" ? unit.symbol : "")}"></label>
                <label data-custom-meters>Meters / unit <input data-movement-meters type="number" min="0.0000001" step="any" value="${unit.kind === "Custom" ? unit.metersPerUnit ?? "" : ""}"></label>
            </div>`;
    }

    private populateMovementControls(): void {
        for (const key of ["perHour", "perWatch", "perMarch"] as MovementKey[]) {
            const row = required<HTMLElement>(this.root, `[data-movement-key="${key}"]`);
            const value = required<HTMLInputElement>(row, "[data-movement-value]");
            const kind = required<HTMLSelectElement>(row, "[data-movement-unit]");
            const symbol = required<HTMLInputElement>(row, "[data-movement-symbol]");
            const meters = required<HTMLInputElement>(row, "[data-movement-meters]");
            const symbolLabel = required<HTMLElement>(row, "[data-custom-symbol]");
            const metersLabel = required<HTMLElement>(row, "[data-custom-meters]");

            const updateVisibility = (): void => {
                const custom = kind.value === "Custom";
                symbolLabel.hidden = !custom;
                metersLabel.hidden = !custom;
                symbol.required = custom && value.value !== "";
            };
            const write = (): void => {
                updateVisibility();
                if (value.value === "") {
                    const movement = this.requireDraft().baseMovement;
                    if (movement) movement[key] = null;
                    this.compactMovement();
                    this.markDirty();
                    return;
                }
                if (!value.validity.valid) {
                    this.markDirty();
                    return;
                }
                const unit = readUnit(kind.value, symbol.value, meters.value);
                const movement = this.ensureMovement();
                movement[key] = { value: Number(value.value), unit };
                this.markDirty();
            };

            value.addEventListener("input", write);
            kind.addEventListener("change", write);
            symbol.addEventListener("input", write);
            meters.addEventListener("input", write);
            updateVisibility();
        }
    }

    private removeMember(memberId: string): void {
        const party = this.requireDraft();
        party.members = party.members.filter(member => member.id !== memberId);
        party.marchingOrder = party.marchingOrder.filter(position => position.memberId !== memberId);
        party.watchList = party.watchList.map(entry => ({
            ...entry,
            memberIds: entry.memberIds.filter(id => id !== memberId)
        }));
        party.activityAssignments = party.activityAssignments.filter(assignment => assignment.participantId !== memberId);

        const sourceMovementContributors = party.movementContributors
            ?? this.runtimeForUnits?.party.movementContributors
            ?? this.getRuntime().party.movementContributors;
        const movementContributors = movementContributorsAfterMemberRemoval(
            sourceMovementContributors,
            memberId);
        if (movementContributors !== undefined) {
            party.movementContributors = movementContributors;
        }

        if (party.baseMovement?.limitingMemberId === memberId) {
            party.baseMovement.limitingMemberId = null;
            this.compactMovement();
        }
        this.markDirty();
        this.renderEditor();
    }

    private ensureMovement(): PartyMovementReference {
        const party = this.requireDraft();
        if (party.baseMovement === null) {
            party.baseMovement = {
                perHour: null,
                perWatch: null,
                perMarch: null,
                limitingMemberId: null,
                note: null
            };
        }
        return party.baseMovement;
    }

    private compactMovement(): void {
        const party = this.requireDraft();
        const movement = party.baseMovement;
        if (!movement) return;
        if (!movement.perHour
            && !movement.perWatch
            && !movement.perMarch
            && !movement.limitingMemberId
            && !movement.note) {
            party.baseMovement = null;
        }
    }

    private preferredUnit(): DistanceUnit {
        const runtime = this.runtimeForUnits ?? this.getRuntime();
        const movement = this.requireDraft().baseMovement;
        const existing = movement?.perHour?.unit
            ?? movement?.perWatch?.unit
            ?? movement?.perMarch?.unit;
        if (existing) return existing;
        if (!runtime.expedition.isSpatial) {
            return { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 };
        }
        return runtime.context.hexCenterDistance?.unit
            ?? runtime.expedition.distanceTraveled.unit;
    }

    private validateDraft(): void {
        const party = this.requireDraft();
        const policy = this.requirePolicy();
        if (party.members.some(member => !member.name.trim())) {
            throw new Error("Every party member requires a name.");
        }
        if (party.standingOrders.some(order => !order.text.trim())) {
            throw new Error("Every standing order requires text.");
        }
        const positions = new Set<string>();
        for (const position of party.marchingOrder) {
            if (position.rank < 0 || position.file < 0) {
                throw new Error("Marching-order rank and file must be non-negative.");
            }
            const key = `${position.rank}:${position.file}`;
            if (positions.has(key)) {
                throw new Error("Marching-order positions must be unique.");
            }
            positions.add(key);
        }
        const watchSlots = new Set<number>();
        for (const watch of party.watchList) {
            if (watch.slot <= 0 || watchSlots.has(watch.slot)) {
                throw new Error("Watch-list slots must be unique positive numbers.");
            }
            watchSlots.add(watch.slot);
        }
        if (policy.support === "None" && party.activityAssignments.length > 0) {
            throw new Error("The current procedure does not define participant activity assignments.");
        }
        if (policy.support === "Supported") {
            const ids = new Set<string>();
            const memberIds = new Set(party.members.map(member => member.id));
            for (const assignment of party.activityAssignments) {
                if (ids.has(assignment.id)) throw new Error("Participant activity assignment ids must be unique.");
                ids.add(assignment.id);
                if (assignment.scope !== policy.assignmentScope) {
                    throw new Error("Participant assignment scope must match the stored procedure policy.");
                }
                if (assignment.participantId !== null && !memberIds.has(assignment.participantId)) {
                    throw new Error("Participant assignments must reference a current party member.");
                }
                if (assignment.activityKey !== null && !policy.activityKeys.includes(assignment.activityKey)) {
                    throw new Error(`Activity ${assignment.activityKey} is not available in the stored procedure.`);
                }
                if (assignment.roleKey !== null && !policy.roleKeys.includes(assignment.roleKey)) {
                    throw new Error(`Role ${assignment.roleKey} is not available in the stored procedure.`);
                }
                validateAssignmentShape(assignment);
            }
        }
        for (const distance of [
            party.baseMovement?.perHour,
            party.baseMovement?.perWatch,
            party.baseMovement?.perMarch
        ]) {
            if (!distance) continue;
            if (!Number.isFinite(distance.value) || distance.value < 0) {
                throw new Error("Movement reference distances must be finite and non-negative.");
            }
            if (distance.unit.kind === "Custom") {
                if (!distance.unit.symbol.trim()) {
                    throw new Error("A custom movement unit requires a symbol.");
                }
                if (distance.unit.metersPerUnit !== null
                    && (!Number.isFinite(distance.unit.metersPerUnit) || distance.unit.metersPerUnit <= 0)) {
                    throw new Error("A custom movement unit conversion must be a finite positive number.");
                }
            }
        }
    }

    private markDirty(): void {
        this.dirty = true;
        this.renderEditorState();
    }

    private renderEditorState(): void {
        const state = this.root.querySelector<HTMLElement>("[data-party-save-state]");
        const save = this.root.querySelector<HTMLButtonElement>("[data-party-save]");
        const reset = this.root.querySelector<HTMLButtonElement>("[data-party-reset]");
        if (!state || !save || !reset) return;
        state.textContent = this.dirty
            ? `Unsaved party changes · session version ${this.latestVersion}`
            : "Saved party sheet";
        save.disabled = !this.dirty;
        reset.disabled = !this.dirty;
    }

    private requirePolicy(): ParticipantActivityPolicy {
        return (this.runtimeForUnits ?? this.getRuntime()).participantActivityPolicy;
    }

    private requireDraft(): ExpeditionParty {
        if (!this.draft) throw new Error("Party sheet is not initialized.");
        return this.draft;
    }
}

function newAssignment(
    policy: ParticipantActivityPolicy,
    party: ExpeditionParty): ParticipantActivityAssignment | null {
    const scope = policy.assignmentScope;
    if (policy.support !== "Supported" || scope === null) return null;
    const participantId = scope === "Party" ? null : party.members[0]?.id ?? null;
    if (scope !== "Party" && participantId === null) return null;

    return {
        id: crypto.randomUUID(),
        scope,
        participantId,
        activityKey: scope === "Role" ? null : policy.activityKeys[0] ?? null,
        roleKey: scope === "Role" ? policy.roleKeys[0] ?? null : null,
        note: null
    };
}

function validateAssignmentShape(assignment: ParticipantActivityAssignment): void {
    if (assignment.scope === "Party") {
        if (assignment.participantId !== null || !assignment.activityKey || assignment.roleKey !== null) {
            throw new Error("Party-wide assignments require an activity and do not target a participant or role.");
        }
        return;
    }
    if (!assignment.participantId) {
        throw new Error("Participant and role assignments require a party member.");
    }
    if (assignment.scope === "Participant" && !assignment.activityKey) {
        throw new Error("Participant assignments require an activity.");
    }
    if (assignment.scope === "Role" && !assignment.roleKey) {
        throw new Error("Role assignments require a role.");
    }
}

function renderReadOnlyAssignments(
    host: HTMLElement,
    assignments: ParticipantActivityAssignment[],
    party: ExpeditionParty): void {
    if (assignments.length === 0) {
        host.append(emptyLine("No typed participant assignment state is stored."));
        return;
    }
    const memberById = new Map(party.members.map(member => [member.id, member.name]));
    for (const assignment of assignments) {
        const line = document.createElement("p");
        line.className = "hc-hint";
        line.textContent = assignmentSummary(assignment, memberById);
        host.append(line);
    }
}

function assignmentSummary(
    assignment: ParticipantActivityAssignment,
    memberById: ReadonlyMap<string, string>): string {
    const target = assignment.participantId
        ? memberById.get(assignment.participantId) ?? "Unknown participant"
        : "Party";
    const activity = assignment.activityKey ? humanizeKey(assignment.activityKey) : null;
    const role = assignment.roleKey ? humanizeKey(assignment.roleKey) : null;
    if (activity && role) return `${target} — ${activity} (${role})`;
    if (activity) return `${target} — ${activity}`;
    if (role) return `${target} — ${role}`;
    return target;
}

function humanizeKey(value: ParticipantActivityAssignmentScope | string): string {
    const text = value.replace(/[-_.]+/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").trim();
    return text ? text[0].toUpperCase() + text.slice(1).toLowerCase() : value;
}

function movementSummary(
    movement: PartyMovementReference | null,
    memberById: ReadonlyMap<string, string>): string {
    if (!movement) return "—";
    const parts = [
        movement.perHour ? `${formatDistance(movement.perHour)}/hour` : null,
        movement.perWatch ? `${formatDistance(movement.perWatch)}/watch` : null,
        movement.perMarch ? `${formatDistance(movement.perMarch)}/march` : null
    ].filter((value): value is string => value !== null);
    if (movement.limitingMemberId) {
        parts.push(`limited by ${memberById.get(movement.limitingMemberId) ?? "unknown member"}`);
    }
    if (movement.note) parts.push(movement.note);
    return parts.join(" · ") || "—";
}

function readUnit(kind: string, symbol: string, meters: string): DistanceUnit {
    if (kind === "Kilometer") return { kind: "Kilometer", symbol: "km", metersPerUnit: 1000 };
    if (kind === "Custom") {
        return {
            kind: "Custom",
            symbol: symbol.trim(),
            metersPerUnit: meters.trim() ? Number(meters) : null
        };
    }
    return { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 };
}

function cloneParty(
    party: ExpeditionParty,
    includeMovementContributors = false): ExpeditionParty {
    const clone: ExpeditionParty = {
        members: party.members.map(member => ({ ...member })),
        marchingOrder: party.marchingOrder.map(position => ({ ...position })),
        watchList: party.watchList.map(entry => ({ ...entry, memberIds: [...entry.memberIds] })),
        standingOrders: party.standingOrders.map(order => ({ ...order })),
        activityAssignments: party.activityAssignments.map(assignment => ({ ...assignment })),
        baseMovement: party.baseMovement
            ? {
                perHour: cloneDistance(party.baseMovement.perHour),
                perWatch: cloneDistance(party.baseMovement.perWatch),
                perMarch: cloneDistance(party.baseMovement.perMarch),
                limitingMemberId: party.baseMovement.limitingMemberId,
                note: party.baseMovement.note
            }
            : null
    };

    if (includeMovementContributors && party.movementContributors !== undefined) {
        clone.movementContributors = party.movementContributors.map(contributor => ({
            ...contributor,
            distanceUnit: contributor.distanceUnit ? { ...contributor.distanceUnit } : null,
            replacesParticipantIds: [...contributor.replacesParticipantIds]
        }));
    }

    return clone;
}

function cloneDistance(distance: DistanceValue | null): DistanceValue | null {
    return distance ? { value: distance.value, unit: { ...distance.unit } } : null;
}

function emptyLine(text: string): HTMLElement {
    const element = document.createElement("p");
    element.className = "hc-hint hc-party-empty-line";
    element.textContent = text;
    return element;
}

function escapeHtml(value: string): string {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

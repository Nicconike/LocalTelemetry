<script>
    import { settings, systemInfo } from "../../lib/store.ts";
    import { deepClone } from "../../lib/utils.ts";
    import SectionCard from "../ui/SectionCard.svelte";

    function busTypeToLabel(t) {
        if (!t) return "DISK";
        t = t.toUpperCase();
        if (t.startsWith("BUSTYPE") || t === "DISK" || t === "SCSI")
            return "DISK";
        return t;
    }

    const staticMetrics = [
        { id: "cpu_pct", label: "CPU Usage" },
        { id: "cpu_temp", label: "CPU Temperature" },
        { id: "cpu_freq", label: "CPU Clock" },
        { id: "cpu_power", label: "CPU Power" },
        { id: "ram_pct", label: "RAM Usage" },
        { id: "ram_used", label: "RAM Used" },
        { id: "gpu_pct", label: "GPU Usage" },
        { id: "gpu_temp", label: "GPU Temperature" },
        { id: "gpu_vram", label: "GPU VRAM" },
        { id: "gpu_freq", label: "GPU Clock" },
        { id: "gpu_power", label: "GPU Power" },
        { id: "net_down", label: "Download Speed" },
        { id: "net_up", label: "Upload Speed" },
        { id: "net_total", label: "Total Traffic" },
        { id: "battery_pct", label: "Battery" },
        { id: "battery_rate", label: "Charge Rate" },
    ];

    function computeDiskMetrics() {
        const disks = $systemInfo.disks || [];
        const typeCount = {};
        for (const d of disks) {
            const t = busTypeToLabel(d.busType);
            typeCount[t] = (typeCount[t] || 0) + 1;
        }
        const typeIdx = {};
        return disks.flatMap((d, i) => {
            const t = busTypeToLabel(d.busType);
            typeIdx[t] = (typeIdx[t] || 0) + 1;
            const num = typeCount[t] > 1 ? String(typeIdx[t]) : "";
            return [
                { id: `disk_disk${i}_read`, label: `${t}${num} Read` },
                { id: `disk_disk${i}_write`, label: `${t}${num} Write` },
            ];
        });
    }
    let diskMetrics = $derived(computeDiskMetrics());

    let allMetrics = $derived([...staticMetrics, ...diskMetrics]);

    function save(items) {
        const next = deepClone($settings);
        next.overlay.row1 = items.filter((x) => x);
        settings.set(next);
    }

    function moveUp(idx) {
        if (!$settings) return;
        const items = ($settings.overlay.row1 || []).filter((x) => x);
        if (idx <= 0 || idx >= items.length) return;
        [items[idx - 1], items[idx]] = [items[idx], items[idx - 1]];
        save(items);
    }

    function moveDown(idx) {
        if (!$settings) return;
        const items = ($settings.overlay.row1 || []).filter((x) => x);
        if (idx < 0 || idx >= items.length - 1) return;
        [items[idx], items[idx + 1]] = [items[idx + 1], items[idx]];
        save(items);
    }

    function remove(idx) {
        if (!$settings) return;
        const items = ($settings.overlay.row1 || []).filter((x) => x);
        if (idx < 0 || idx >= items.length) return;
        items.splice(idx, 1);
        save(items);
    }

    function add(metricId) {
        if (!metricId || !$settings) return;
        const items = ($settings.overlay.row1 || []).filter((x) => x);
        if (items.includes(metricId)) return;
        items.push(metricId);
        save(items);
    }

    function formatMetric(id) {
        const m = allMetrics.find((x) => x.id === id);
        return m ? m.label : id;
    }

    let dragIdx = $state(-1);
    let dropIdx = $state(-1);

    function handleDragStart(e, idx) {
        dragIdx = idx;
        e.dataTransfer.effectAllowed = "move";
        e.dataTransfer.setData("text/plain", idx);
    }

    function handleDragOver(e, idx) {
        e.preventDefault();
        e.dataTransfer.dropEffect = "move";
        dropIdx = idx;
    }

    function handleDragLeave() {
        dropIdx = -1;
    }

    function handleDrop(e, idx) {
        e.preventDefault();
        if (dragIdx < 0 || dragIdx === idx) {
            dragIdx = -1;
            dropIdx = -1;
            return;
        }
        const items = ($settings.overlay.row1 || []).filter((x) => x);
        if (
            dragIdx < 0 ||
            dragIdx >= items.length ||
            idx < 0 ||
            idx >= items.length
        ) {
            dragIdx = -1;
            dropIdx = -1;
            return;
        }
        const [moved] = items.splice(dragIdx, 1);
        items.splice(idx, 0, moved);
        save(items);
        dragIdx = -1;
        dropIdx = -1;
    }

    function handleDragEnd() {
        dragIdx = -1;
        dropIdx = -1;
    }

    let items = $derived(
        $settings ? ($settings.overlay.row1 || []).filter((x) => x) : [],
    );
    let available = $derived(allMetrics.filter((m) => !items.includes(m.id)));

    function handleAdd(e) {
        const el = e.currentTarget;
        const metricId = el.value;
        if (!metricId) return;
        add(metricId);
        el.value = "";
    }
</script>

{#if $settings}
    <div class="page">
        <div class="page-header">
            <h2>Widget Metric Layout</h2>
            <p class="desc">
                Add, remove and reorder the metrics shown in the widget. The
                order of the metrics here is the order they will be displayed in
                the overlay
            </p>
        </div>

        <SectionCard title="Metrics">
            {#each items as id, idx}
                <div
                    class="metric-row"
                    role="listitem"
                    draggable="true"
                    class:dragging={dragIdx === idx}
                    class:drag-over={dropIdx === idx && dragIdx !== idx}
                    ondragstart={(e) => handleDragStart(e, idx)}
                    ondragover={(e) => handleDragOver(e, idx)}
                    ondragleave={handleDragLeave}
                    ondrop={(e) => handleDrop(e, idx)}
                    ondragend={handleDragEnd}
                >
                    <span class="drag-handle">&#x2630;</span>
                    <span class="metric-idx">{idx + 1}.</span>
                    <span class="metric-name">{formatMetric(id)}</span>
                    <div class="metric-actions">
                        <button
                            class="btn-icon"
                            title="Move up"
                            disabled={idx === 0}
                            onclick={() => moveUp(idx)}>&#x25B2;</button
                        >
                        <button
                            class="btn-icon"
                            title="Move down"
                            disabled={idx >= items.length - 1}
                            onclick={() => moveDown(idx)}>&#x25BC;</button
                        >
                        <button
                            class="btn-icon btn-remove"
                            title="Remove"
                            onclick={() => remove(idx)}>&#x2716;</button
                        >
                    </div>
                </div>
            {/each}

            {#if available.length > 0}
                <div class="add-row">
                    <span class="add-label">+ Add metric:</span>
                    <select class="add-select" onchange={handleAdd}>
                        <option value="">-- Select --</option>
                        {#each available as m (m.id)}
                            <option value={m.id}>{m.label}</option>
                        {/each}
                    </select>
                </div>
            {/if}

            {#if items.length === 0}
                <p class="empty">No metrics configured. Add one above.</p>
            {/if}
        </SectionCard>
    </div>
{/if}

<style>
    .page {
        display: flex;
        flex-direction: column;
        gap: var(--space-5);
    }
    .page-header {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
    }
    h2 {
        font-size: var(--text-lg);
        font-weight: 600;
    }
    .desc {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
    }

    .metric-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        margin-bottom: var(--space-2);
        cursor: grab;
        transition:
            opacity 0.15s,
            border-color 0.15s,
            background 0.15s;
    }
    .metric-row:active {
        cursor: grabbing;
    }
    .metric-row.dragging {
        opacity: 0.4;
    }
    .metric-row.drag-over {
        border-color: var(--color-primary);
        background: color-mix(
            in srgb,
            var(--color-primary) 15%,
            var(--color-bg)
        );
    }
    .drag-handle {
        font-size: 14px;
        color: var(--color-text-muted);
        cursor: grab;
        flex-shrink: 0;
        line-height: 1;
        user-select: none;
    }
    .metric-idx {
        font-size: var(--text-xs);
        color: var(--color-text-muted);
        min-width: 20px;
        flex-shrink: 0;
    }
    .metric-name {
        flex: 1;
        font-size: var(--text-sm);
        font-weight: 500;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .metric-actions {
        display: flex;
        gap: var(--space-1);
    }

    .btn-icon {
        background: var(--color-surface-2);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        padding: 2px 6px;
        cursor: pointer;
        font-size: 11px;
        color: var(--color-text-muted);
        transition: all var(--transition);
    }
    .btn-icon:hover:not(:disabled) {
        color: var(--color-text);
        border-color: var(--color-text-muted);
    }
    .btn-icon:disabled {
        opacity: 0.3;
        cursor: not-allowed;
    }
    .btn-remove:hover:not(:disabled) {
        color: #f87171;
        border-color: #f87171;
    }

    .add-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        margin-top: var(--space-2);
    }
    .add-label {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        white-space: nowrap;
    }
    .add-select {
        flex: 1;
        padding: var(--space-1) var(--space-2);
        border-radius: var(--radius-sm);
        border: 1px solid var(--color-border);
        background: var(--color-bg);
        color: var(--color-text);
        font-size: var(--text-sm);
    }
    .empty {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        text-align: center;
        padding: var(--space-4);
    }
</style>

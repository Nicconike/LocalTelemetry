<script>
    import { settings, systemInfo } from "../../lib/store.ts";
    import { deepClone } from "../../lib/utils.ts";

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
        { id: "gpu_pct", label: "GPU Usage" },
        { id: "gpu_temp", label: "GPU Temperature" },
        { id: "gpu_vram", label: "GPU VRAM" },
        { id: "gpu_freq", label: "GPU Clock" },
        { id: "gpu_power", label: "GPU Power" },
        { id: "ram_pct", label: "RAM Usage" },
        { id: "ram_used", label: "RAM Used" },
        { id: "net_down", label: "Download Speed" },
        { id: "net_up", label: "Upload Speed" },
        { id: "net_total", label: "Total Traffic" },
    ];

    function computeDiskMetrics() {
        const disks = $systemInfo.disks || [];
        const typeCount = {};
        for (const d of disks) {
            const t = d.displayType || busTypeToLabel(d.busType);
            typeCount[t] = (typeCount[t] || 0) + 1;
        }
        const typeIdx = {};
        return disks.flatMap((d, i) => {
            const t = d.displayType || busTypeToLabel(d.busType);
            typeIdx[t] = (typeIdx[t] || 0) + 1;
            const num = typeCount[t] > 1 ? ` ${typeIdx[t]}` : "";
            return [
                { id: `disk_disk${i}_read`, label: `${t}${num} Read` },
                { id: `disk_disk${i}_write`, label: `${t}${num} Write` },
            ];
        });
    }
    let diskMetrics = $derived(computeDiskMetrics());

    const batteryMetrics = [
        { id: "battery_pct", label: "Battery" },
        { id: "battery_rate", label: "Charge Rate" },
    ];

    let allMetrics = $derived([
        ...staticMetrics,
        ...diskMetrics,
        ...($settings?.monitoring?.hasBattery ? batteryMetrics : []),
    ]);

    const defaultMetrics = [
        "cpu_pct",
        "cpu_temp",
        "gpu_pct",
        "gpu_temp",
        "gpu_vram",
        "net_total",
        "ram_pct",
        "ram_used",
        "net_down",
        "net_up",
    ];

    const gamingMetrics = [
        "cpu_pct",
        "cpu_temp",
        "cpu_freq",
        "cpu_power",
        "gpu_pct",
        "gpu_temp",
        "gpu_freq",
        "gpu_power",
        "ram_pct",
        "gpu_vram",
        "net_down",
        "net_up",
    ];

    const compactMetrics = [
        "cpu_pct",
        "gpu_pct",
        "ram_pct",
        "gpu_vram",
        "net_down",
        "net_up",
    ];

    const thermalsMetrics = [
        "cpu_pct",
        "cpu_temp",
        "cpu_freq",
        "cpu_power",
        "gpu_pct",
        "gpu_temp",
        "gpu_freq",
        "gpu_power",
    ];

    function getWorkstationMetrics() {
        const readId = diskMetrics[0]?.id || "disk_disk0_read";
        const writeId = diskMetrics[1]?.id || "disk_disk0_write";
        return [
            "cpu_pct",
            "cpu_temp",
            "ram_pct",
            "ram_used",
            "net_down",
            "net_up",
            readId,
            writeId,
        ];
    }

    const networkMetrics = [
        "cpu_pct",
        "ram_pct",
        "net_down",
        "net_up",
        "net_total",
        "ram_used",
    ];

    const microMetrics = [
        "cpu_pct",
        "gpu_pct",
        "ram_pct",
        "net_down",
    ];

    function getFullMetrics() {
        const base = [
            "cpu_pct",
            "cpu_temp",
            "cpu_freq",
            "cpu_power",
            "gpu_pct",
            "gpu_temp",
            "gpu_freq",
            "gpu_power",
            "ram_pct",
            "ram_used",
            "gpu_vram",
            "net_total",
            "net_down",
            "net_up",
        ];
        const diskIds = diskMetrics.map((d) => d.id);
        const battIds = $settings?.monitoring?.hasBattery
            ? batteryMetrics.map((b) => b.id)
            : [];
        return [...base, ...diskIds, ...battIds];
    }

    let presets = $derived([
        {
            id: "default",
            label: "Default",
            tooltip: "Standard balanced layout for everyday system monitoring",
            count: defaultMetrics.length,
        },
        {
            id: "gaming",
            label: "Gaming",
            tooltip: "CPU and GPU clock and wattage telemetry with memory pool",
            count: gamingMetrics.length,
        },
        {
            id: "compact",
            label: "Compact",
            tooltip: "Minimal taskbar footprint with core utilization and network speed",
            count: compactMetrics.length,
        },
        {
            id: "micro",
            label: "Micro",
            tooltip: "Ultra-compact 2-column strip for narrow taskbars and small displays",
            count: microMetrics.length,
        },
        {
            id: "thermals",
            label: "Thermals & Power",
            tooltip: "Dual-column CPU and GPU thermal, wattage, and clock diagnostics",
            count: thermalsMetrics.length,
        },
        {
            id: "workstation",
            label: "Workstation",
            tooltip: "Productivity pairing: CPU compute, RAM pool, live network, and primary drive I/O",
            count: getWorkstationMetrics().length,
        },
        {
            id: "network",
            label: "Network & Traffic",
            tooltip: "Bandwidth focus: live download/upload speeds, cumulative transfer, and core load",
            count: networkMetrics.length,
        },
        {
            id: "full",
            label: "Full Telemetry",
            tooltip: "Comprehensive hardware overview across all sensors, drives, and battery",
            getCount: () => getFullMetrics().length,
        },
    ]);

    function getPresetMetrics(id) {
        switch (id) {
            case "gaming":
                return [...gamingMetrics];
            case "compact":
                return [...compactMetrics];
            case "micro":
                return [...microMetrics];
            case "thermals":
                return [...thermalsMetrics];
            case "workstation":
                return getWorkstationMetrics();
            case "network":
                return [...networkMetrics];
            case "full":
                return getFullMetrics();
            case "default":
            default:
                return [...defaultMetrics];
        }
    }

    let items = $derived(
        $settings ? ($settings.overlay.row1 || []).filter((x) => x) : [],
    );

    let activePreset = $derived.by(() => {
        const cur = items.join(",");
        if (cur === defaultMetrics.join(",")) return "default";
        if (cur === gamingMetrics.join(",")) return "gaming";
        if (cur === compactMetrics.join(",")) return "compact";
        if (cur === microMetrics.join(",")) return "micro";
        if (cur === thermalsMetrics.join(",")) return "thermals";
        if (cur === getWorkstationMetrics().join(",")) return "workstation";
        if (cur === networkMetrics.join(",")) return "network";
        if (cur === getFullMetrics().join(",")) return "full";
        return "custom";
    });

    let columns = $derived.by(() => {
        const cols = [];
        for (let i = 0; i < items.length; i += 2) {
            cols.push({
                colIdx: Math.floor(i / 2),
                topIdx: i,
                topId: items[i],
                botIdx: i + 1 < items.length ? i + 1 : -1,
                botId: i + 1 < items.length ? items[i + 1] : null,
            });
        }
        return cols;
    });

    function applyPreset(presetId) {
        save(getPresetMetrics(presetId), presetId);
    }

    function save(nextItems, presetId) {
        const next = deepClone($settings);
        const filtered = nextItems.filter((x) => x);
        next.overlay.row1 = filtered;
        if (presetId) {
            next.overlay.layoutPreset = presetId;
        } else {
            const cur = filtered.join(",");
            if (cur === defaultMetrics.join(",")) next.overlay.layoutPreset = "default";
            else if (cur === gamingMetrics.join(",")) next.overlay.layoutPreset = "gaming";
            else if (cur === compactMetrics.join(",")) next.overlay.layoutPreset = "compact";
            else if (cur === microMetrics.join(",")) next.overlay.layoutPreset = "micro";
            else if (cur === thermalsMetrics.join(",")) next.overlay.layoutPreset = "thermals";
            else if (cur === getWorkstationMetrics().join(",")) next.overlay.layoutPreset = "workstation";
            else if (cur === networkMetrics.join(",")) next.overlay.layoutPreset = "network";
            else if (cur === getPresetMetrics("full").join(",")) next.overlay.layoutPreset = "full";
            else next.overlay.layoutPreset = "custom";
        }
        settings.set(next);
    }

    function resetLayout() {
        save([...defaultMetrics], "default");
    }

    function moveUp(idx) {
        if (!$settings) return;
        const cur = ($settings.overlay.row1 || []).filter((x) => x);
        if (idx <= 0 || idx >= cur.length) return;
        [cur[idx - 1], cur[idx]] = [cur[idx], cur[idx - 1]];
        save(cur, "custom");
    }

    function moveDown(idx) {
        if (!$settings) return;
        const cur = ($settings.overlay.row1 || []).filter((x) => x);
        if (idx < 0 || idx >= cur.length - 1) return;
        [cur[idx], cur[idx + 1]] = [cur[idx + 1], cur[idx]];
        save(cur, "custom");
    }

    function remove(idx) {
        if (!$settings) return;
        const cur = ($settings.overlay.row1 || []).filter((x) => x);
        if (idx < 0 || idx >= cur.length) return;
        cur.splice(idx, 1);
        save(cur, "custom");
    }

    function add(metricId) {
        if (!metricId || !$settings) return;
        const cur = ($settings.overlay.row1 || []).filter((x) => x);
        if (cur.includes(metricId)) return;
        cur.push(metricId);
        save(cur, "custom");
    }

    function swapColumn(colIdx) {
        const top = 2 * colIdx;
        const bot = top + 1;
        if (bot >= items.length) return;
        const next = [...items];
        [next[top], next[bot]] = [next[bot], next[top]];
        save(next, "custom");
    }

    function moveColumn(colIdx, direction) {
        const targetCol = colIdx + direction;
        if (targetCol < 0) return;
        const cols = [];
        for (let i = 0; i < items.length; i += 2) {
            const pair = [items[i]];
            if (i + 1 < items.length) pair.push(items[i + 1]);
            cols.push(pair);
        }
        if (targetCol >= cols.length) return;
        [cols[colIdx], cols[targetCol]] = [cols[targetCol], cols[colIdx]];
        save(cols.flat(), "custom");
    }

    function formatMetric(id) {
        const m = allMetrics.find((x) => x.id === id);
        return m ? m.label : id;
    }

    function formatShortMetric(id) {
        const shortMap = {
            cpu_pct: "CPU %",
            cpu_temp: "CPU Temp",
            cpu_freq: "CPU Clock",
            cpu_power: "CPU Power",
            gpu_pct: "GPU %",
            gpu_temp: "GPU Temp",
            gpu_vram: "VRAM",
            gpu_freq: "GPU Clock",
            gpu_power: "GPU Power",
            ram_pct: "RAM %",
            ram_used: "RAM Used",
            net_down: "Net Down",
            net_up: "Net Up",
            net_total: "Traffic",
            battery_pct: "Battery",
            battery_rate: "Charge",
        };
        if (shortMap[id]) return shortMap[id];
        if (id && id.startsWith("disk_")) {
            const m = allMetrics.find((x) => x.id === id);
            if (m) return m.label;
            const isRead = id.endsWith("_read");
            return `Disk ${isRead ? "Read" : "Write"}`;
        }
        return formatMetric(id);
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
        const cur = ($settings.overlay.row1 || []).filter((x) => x);
        if (
            dragIdx < 0 ||
            dragIdx >= cur.length ||
            idx < 0 ||
            idx >= cur.length
        ) {
            dragIdx = -1;
            dropIdx = -1;
            return;
        }
        const [moved] = cur.splice(dragIdx, 1);
        cur.splice(idx, 0, moved);
        save(cur, "custom");
        dragIdx = -1;
        dropIdx = -1;
    }

    function handleDragEnd() {
        dragIdx = -1;
        dropIdx = -1;
    }

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
                Configure your dual-row taskbar telemetry. Even metrics form the top row, and odd metrics form the bottom row of each column.
            </p>
        </div>

        <div class="preset-section">
            <div class="preset-header">
                <span class="preset-label">Layout Presets</span>
                {#if activePreset === "custom"}
                    <span class="custom-indicator custom-card" role="status">
                        Custom ({items.length} metrics)
                    </span>
                {/if}
            </div>
            <div class="preset-tiles" role="radiogroup" aria-label="Layout Presets">
                {#each presets as p (p.id)}
                    <button
                        type="button"
                        class="preset-tile preset-pill preset-card-item"
                        class:active={activePreset === p.id}
                        onclick={() => applyPreset(p.id)}
                        role="radio"
                        aria-checked={activePreset === p.id}
                        title={p.tooltip}
                    >
                        <span class="tile-title">{p.label}</span>
                        <span class="tile-count">
                            {p.getCount ? p.getCount() : p.count}
                        </span>
                    </button>
                {/each}
            </div>
        </div>

        <div class="metrics-section">
            {#if columns.length > 0}
                <div class="mini-preview-card">
                    <div class="mini-preview-head">
                        <span class="mini-preview-label">Live Taskbar Widget Preview</span>
                        <span class="mini-preview-sub">2-row column preview</span>
                    </div>
                    <div class="mini-taskbar-widget" role="region" aria-label="Taskbar Widget Preview">
                        {#each columns as col (col.colIdx)}
                            <div class="mini-col">
                                <div class="mini-row top">
                                    <span class="mini-dot top"></span>
                                    <span class="mini-text">{formatShortMetric(col.topId)}</span>
                                </div>
                                <div class="mini-row bot">
                                    {#if col.botId}
                                        <span class="mini-dot bot"></span>
                                        <span class="mini-text">{formatShortMetric(col.botId)}</span>
                                    {:else}
                                        <span class="mini-empty">-</span>
                                    {/if}
                                </div>
                            </div>
                        {/each}
                    </div>
                </div>

                <div class="columns-container">
                    {#each columns as col (col.colIdx)}
                        <div class="column-card">
                            <div class="column-head">
                                <div class="column-title-group">
                                    <span class="column-tag">Column {col.colIdx + 1}</span>
                                    <span class="column-desc">
                                        {formatMetric(col.topId)}{col.botId ? ` / ${formatMetric(col.botId)}` : ""}
                                    </span>
                                </div>
                                <div class="column-actions">
                                    {#if col.botId}
                                        <button
                                            type="button"
                                            class="btn-col-action btn-swap"
                                            title="Swap Top and Bottom row metrics"
                                            onclick={() => swapColumn(col.colIdx)}
                                        >
                                            ⇅ Swap
                                        </button>
                                    {/if}
                                    <button
                                        type="button"
                                        class="btn-col-action"
                                        title="Move column left"
                                        disabled={col.colIdx === 0}
                                        onclick={() => moveColumn(col.colIdx, -1)}
                                    >
                                        ◀ Col
                                    </button>
                                    <button
                                        type="button"
                                        class="btn-col-action"
                                        title="Move column right"
                                        disabled={col.colIdx >= columns.length - 1}
                                        onclick={() => moveColumn(col.colIdx, 1)}
                                    >
                                        Col ▶
                                    </button>
                                </div>
                            </div>

                            <!-- Top Row Metric -->
                            <div
                                class="metric-row"
                                role="listitem"
                                draggable="true"
                                class:dragging={dragIdx === col.topIdx}
                                class:drag-over={dropIdx === col.topIdx && dragIdx !== col.topIdx}
                                ondragstart={(e) => handleDragStart(e, col.topIdx)}
                                ondragover={(e) => handleDragOver(e, col.topIdx)}
                                ondragleave={handleDragLeave}
                                ondrop={(e) => handleDrop(e, col.topIdx)}
                                ondragend={handleDragEnd}
                            >
                                <span class="drag-handle">&#x2630;</span>
                                <span class="metric-idx">{col.topIdx + 1}.</span>
                                <span class="row-badge top">Top Row</span>
                                <span class="metric-name">{formatMetric(col.topId)}</span>
                                <div class="metric-actions">
                                    <button
                                        class="btn-icon"
                                        title="Move up"
                                        disabled={col.topIdx === 0}
                                        onclick={() => moveUp(col.topIdx)}>&#x25B2;</button
                                    >
                                    <button
                                        class="btn-icon"
                                        title="Move down"
                                        disabled={col.topIdx >= items.length - 1}
                                        onclick={() => moveDown(col.topIdx)}>&#x25BC;</button
                                    >
                                    <button
                                        class="btn-icon btn-remove"
                                        title="Remove"
                                        onclick={() => remove(col.topIdx)}>&#x2716;</button
                                    >
                                </div>
                            </div>

                            <!-- Bottom Row Metric -->
                            {#if col.botId}
                                <div
                                    class="metric-row"
                                    role="listitem"
                                    draggable="true"
                                    class:dragging={dragIdx === col.botIdx}
                                    class:drag-over={dropIdx === col.botIdx && dragIdx !== col.botIdx}
                                    ondragstart={(e) => handleDragStart(e, col.botIdx)}
                                    ondragover={(e) => handleDragOver(e, col.botIdx)}
                                    ondragleave={handleDragLeave}
                                    ondrop={(e) => handleDrop(e, col.botIdx)}
                                    ondragend={handleDragEnd}
                                >
                                    <span class="drag-handle">&#x2630;</span>
                                    <span class="metric-idx">{col.botIdx + 1}.</span>
                                    <span class="row-badge bottom">Bottom Row</span>
                                    <span class="metric-name">{formatMetric(col.botId)}</span>
                                    <div class="metric-actions">
                                        <button
                                            class="btn-icon"
                                            title="Move up"
                                            disabled={col.botIdx === 0}
                                            onclick={() => moveUp(col.botIdx)}>&#x25B2;</button
                                        >
                                        <button
                                            class="btn-icon"
                                            title="Move down"
                                            disabled={col.botIdx >= items.length - 1}
                                            onclick={() => moveDown(col.botIdx)}>&#x25BC;</button
                                        >
                                        <button
                                            class="btn-icon btn-remove"
                                            title="Remove"
                                            onclick={() => remove(col.botIdx)}>&#x2716;</button
                                        >
                                    </div>
                                </div>
                            {:else}
                                <div class="col-slot empty-slot">
                                    <span class="empty-slot-text">Empty Bottom Row (add a metric below to pair)</span>
                                </div>
                            {/if}
                        </div>
                    {/each}
                </div>
            {:else}
                <p class="empty">No metrics configured. Add one above.</p>
            {/if}

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

            <div class="footer-actions">
                <button
                    class="reset-btn"
                    onclick={resetLayout}
                    disabled={items.join(",") === defaultMetrics.join(",")}
                >
                    Reset Layout to Defaults
                </button>
            </div>
        </div>
    </div>
{/if}

<style>
    .page {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
    }
    .page-header {
        display: flex;
        flex-direction: column;
        gap: 2px;
    }
    h2 {
        font-size: var(--text-lg);
        font-weight: 600;
    }
    .desc {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
    }

    .preset-section {
        display: flex;
        flex-direction: column;
        gap: 6px;
    }
    .preset-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 0 2px;
    }
    .preset-label {
        font-size: var(--text-xs);
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 0.6px;
        color: var(--color-text-muted);
    }
    .custom-indicator {
        font-size: 11px;
        font-weight: 600;
        color: var(--color-primary);
        background: color-mix(in srgb, var(--color-primary) 12%, transparent);
        border: 1px solid color-mix(in srgb, var(--color-primary) 30%, transparent);
        padding: 2px 8px;
        border-radius: 9999px;
        letter-spacing: 0.2px;
    }

    .preset-tiles {
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: 6px;
    }
    .preset-tile {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-2);
        padding: 6px 10px;
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        cursor: pointer;
        min-width: 0;
        transition:
            border-color var(--transition),
            background-color var(--transition),
            box-shadow var(--transition);
        user-select: none;
        text-align: left;
    }
    .preset-tile:hover:not(.active) {
        border-color: var(--color-text-muted);
        background: var(--color-surface-2, var(--color-surface));
    }
    .preset-tile.active {
        border-color: var(--color-primary);
        background: color-mix(
            in srgb,
            var(--color-primary) 10%,
            var(--color-surface)
        );
        box-shadow: 0 0 0 1px var(--color-primary);
    }
    .tile-title {
        font-size: var(--text-xs);
        font-weight: 600;
        color: var(--color-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }
    .preset-tile.active .tile-title {
        color: var(--color-primary);
    }
    .tile-count {
        font-size: 11px;
        font-weight: 500;
        color: var(--color-text-muted);
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        border-radius: 9999px;
        padding: 1px 6px;
        line-height: 1.2;
        flex-shrink: 0;
    }
    .preset-tile.active .tile-count {
        border-color: color-mix(in srgb, var(--color-primary) 40%, transparent);
        color: var(--color-primary);
        background: color-mix(in srgb, var(--color-primary) 15%, var(--color-bg));
    }

    .metrics-section {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
    }

    /* Live Mini Taskbar Preview */
    .mini-preview-card {
        display: flex;
        flex-direction: column;
        gap: 6px;
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        padding: 8px 12px;
        margin-bottom: 0;
    }
    .mini-preview-head {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: var(--space-2);
    }
    .mini-preview-label {
        font-size: 11px;
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 0.5px;
        color: var(--color-text-muted);
    }
    .mini-preview-sub {
        font-size: 11px;
        color: var(--color-text-muted);
        opacity: 0.8;
    }
    .mini-taskbar-widget {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        overflow-x: auto;
        padding: var(--space-2) var(--space-3);
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        user-select: none;
        scrollbar-width: thin;
        scrollbar-color: transparent transparent;
        transition: scrollbar-color var(--transition);
    }
    .mini-taskbar-widget:hover,
    .mini-taskbar-widget:focus-within {
        scrollbar-color: var(--color-border) transparent;
    }
    .mini-taskbar-widget::-webkit-scrollbar {
        height: 4px;
    }
    .mini-taskbar-widget::-webkit-scrollbar-button {
        display: none;
        width: 0;
        height: 0;
    }
    .mini-taskbar-widget::-webkit-scrollbar-track {
        background: transparent;
    }
    .mini-taskbar-widget::-webkit-scrollbar-thumb {
        background: transparent;
        border-radius: var(--radius-full);
        transition: background-color var(--transition);
    }
    .mini-taskbar-widget:hover::-webkit-scrollbar-thumb,
    .mini-taskbar-widget:focus-within::-webkit-scrollbar-thumb {
        background: var(--color-border);
    }
    .mini-taskbar-widget::-webkit-scrollbar-thumb:hover {
        background: var(--color-text-faint);
    }
    .mini-col {
        display: flex;
        flex-direction: column;
        gap: 3px;
        padding: 4px 8px;
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        flex-shrink: 0;
        min-width: 65px;
    }
    .mini-row {
        display: flex;
        align-items: center;
        gap: 5px;
        font-size: 11px;
        line-height: 1.2;
    }
    .mini-dot {
        width: 6px;
        height: 6px;
        border-radius: 9999px;
        flex-shrink: 0;
    }
    .mini-dot.top {
        background: var(--color-primary);
        box-shadow: 0 0 3px color-mix(in srgb, var(--color-primary) 60%, transparent);
    }
    .mini-dot.bot {
        background: var(--color-secondary);
        box-shadow: 0 0 3px color-mix(in srgb, var(--color-secondary) 60%, transparent);
    }
    .mini-text {
        font-weight: 600;
        white-space: nowrap;
        color: var(--color-text);
        font-size: 11px;
    }
    .mini-empty {
        font-size: 10px;
        color: var(--color-text-muted);
        font-style: italic;
    }

    /* Columns Container & Column Cards */
    .columns-container {
        display: flex;
        flex-direction: column;
        gap: 8px;
    }
    .column-card {
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        padding: 8px 10px;
        display: flex;
        flex-direction: column;
        gap: 6px;
        transition: border-color var(--transition);
    }
    .column-card:hover {
        border-color: color-mix(in srgb, var(--color-primary) 30%, var(--color-border));
    }
    .column-head {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding-bottom: var(--space-1);
        border-bottom: 1px solid var(--color-divider, rgba(255, 255, 255, 0.05));
    }
    .column-title-group {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        overflow: hidden;
    }
    .column-tag {
        font-size: 11px;
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 0.5px;
        color: var(--color-text);
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        padding: 1px 7px;
        border-radius: var(--radius-sm);
        flex-shrink: 0;
    }
    .column-desc {
        font-size: 12px;
        color: var(--color-text-muted);
        font-weight: 500;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }
    .column-actions {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        flex-shrink: 0;
    }
    .btn-col-action {
        font-size: 11px;
        font-weight: 600;
        padding: 2px 7px;
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        color: var(--color-text-muted);
        cursor: pointer;
        transition: all var(--transition);
        white-space: nowrap;
    }
    .btn-col-action:hover:not(:disabled) {
        color: var(--color-text);
        border-color: var(--color-text-muted);
    }
    .btn-col-action:disabled {
        opacity: 0.3;
        cursor: not-allowed;
    }
    .btn-col-action.btn-swap {
        color: var(--color-primary);
        border-color: color-mix(in srgb, var(--color-primary) 40%, transparent);
        background: color-mix(in srgb, var(--color-primary) 8%, var(--color-bg));
    }
    .btn-col-action.btn-swap:hover {
        background: color-mix(in srgb, var(--color-primary) 18%, var(--color-bg));
        border-color: var(--color-primary);
    }

    .row-badge {
        font-size: 10px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.4px;
        padding: 2px 6px;
        border-radius: var(--radius-sm);
        white-space: nowrap;
        flex-shrink: 0;
        user-select: none;
    }
    .row-badge.top {
        background: color-mix(in srgb, var(--color-primary) 14%, transparent);
        color: var(--color-primary);
        border: 1px solid color-mix(in srgb, var(--color-primary) 32%, transparent);
    }
    .row-badge.bottom {
        background: color-mix(in srgb, var(--color-secondary) 14%, transparent);
        color: var(--color-secondary);
        border: 1px solid color-mix(in srgb, var(--color-secondary) 32%, transparent);
    }

    .metric-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: 5px 8px;
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
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
    .col-slot.empty-slot {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border: 1px dashed var(--color-border);
        border-radius: var(--radius-sm);
        opacity: 0.6;
        cursor: default;
        justify-content: center;
        background: transparent;
    }
    .empty-slot-text {
        font-size: var(--text-xs);
        color: var(--color-text-muted);
        font-style: italic;
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
        color: var(--color-error);
        border-color: var(--color-error);
    }

    .add-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        margin-top: var(--space-3);
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
    .footer-actions {
        margin-top: var(--space-4);
        padding-top: var(--space-3);
        border-top: 1px solid var(--color-divider, rgba(255, 255, 255, 0.08));
    }
    .reset-btn {
        padding: var(--space-1) var(--space-3);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        border: 1px solid var(--color-border);
        background: transparent;
        cursor: pointer;
        transition: all var(--transition);
    }
    .reset-btn:hover {
        color: var(--color-text);
        border-color: var(--color-text-muted);
    }
    .reset-btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }

    @media (max-width: 600px) {
        .preset-tiles {
            grid-template-columns: repeat(2, 1fr);
        }
    }
</style>

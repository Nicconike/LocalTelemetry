<script>
    import {
        allTrafficHistory,
        trafficHistory,
        trafficToday,
        importResult,
    } from "../../lib/store.ts";
    import { formatTrafficBytes } from "../../lib/utils.ts";
    import { importDatContent } from "../../lib/bridge.ts";

    const _initDate = new Date();
    let currentYear = $state(_initDate.getFullYear());
    let currentMonth = $state(_initDate.getMonth() + 1);
    let calendarDays = $state([]);
    let selectedDay = $state(null);
    let totalDown = $state(0);
    let totalUp = $state(0);
    let activeFilter = $state("all");
    let availableTypes = $state(["all"]);
    let availableTypesDerived = $derived([
        "all",
        ...new Set($trafficHistory.map((r) => r.interfaceName).filter(Boolean)),
    ]);
    $effect(() => {
        availableTypes = availableTypesDerived;
    });

    let monthLabel = $derived(
        new Date(currentYear, currentMonth - 1, 1).toLocaleString("default", {
            year: "numeric",
            month: "long",
        }),
    );

    function prevMonth() {
        if (currentMonth === 1) {
            currentMonth = 12;
            currentYear--;
        } else currentMonth--;
    }

    function nextMonth() {
        if (currentMonth === 12) {
            currentMonth = 1;
            currentYear++;
        } else currentMonth++;
    }

    function getTierClass(totalBytes) {
        if (totalBytes === 0) return "tier-none";
        const GB = 1_000_000_000;
        if (totalBytes <= 10 * GB) return "tier-blue";
        if (totalBytes <= 50 * GB) return "tier-low";
        if (totalBytes <= 100 * GB) return "tier-med";
        if (totalBytes <= 150 * GB) return "tier-orange";
        if (totalBytes <= 200 * GB) return "tier-high";
        return "tier-max";
    }

    function filteredRecords() {
        if (activeFilter === "all") return $trafficHistory;
        return $trafficHistory.filter((r) => r.interfaceName === activeFilter);
    }

    function setFilter(type) {
        activeFilter = type;
    }

    function buildCalendar() {
        const records = filteredRecords();
        const dim = new Date(currentYear, currentMonth, 0).getDate();
        const dateGroups = {};
        for (const r of records) {
            if (!dateGroups[r.date]) {
                dateGroups[r.date] = { downBytes: 0, upBytes: 0, records: [] };
            }
            dateGroups[r.date].downBytes += r.downBytes;
            dateGroups[r.date].upBytes += r.upBytes;
            dateGroups[r.date].records.push({
                interfaceType: r.interfaceName,
                downBytes: r.downBytes,
                upBytes: r.upBytes,
            });
        }

        const d = new Date(currentYear, currentMonth - 1, 1).getDay();
        const firstCol = d === 0 ? 6 : d - 1;

        const days = [];
        for (let i = 0; i < firstCol; i++) days.push(null);

        for (let day = 1; day <= dim; day++) {
            const dateStr = `${String(day).padStart(2, "0")}/${String(currentMonth).padStart(2, "0")}/${currentYear}`;
            const group = dateGroups[dateStr];
            const down = group ? group.downBytes : 0;
            const up = group ? group.upBytes : 0;
            const total = down + up;

            const isToday =
                day === new Date().getDate() &&
                currentMonth === new Date().getMonth() + 1 &&
                currentYear === new Date().getFullYear();

            days.push({
                day,
                date: dateStr,
                downBytes: down,
                upBytes: up,
                totalBytes: total,
                isToday,
                records: group ? group.records : [],
            });
        }

        calendarDays = days;
        totalDown = records.reduce((s, r) => s + r.downBytes, 0);
        totalUp = records.reduce((s, r) => s + r.upBytes, 0);
    }

    function loadMonth() {
        selectedDay = null;
        const monthRecords = $allTrafficHistory.filter((r) => {
            const p = r.date.split("/");
            return (
                p.length === 3 &&
                parseInt(p[2]) === currentYear &&
                parseInt(p[1]) === currentMonth
            );
        });
        trafficHistory.set(monthRecords);
    }

    $effect(() => {
        if ($allTrafficHistory.length >= 0) loadMonth();
    });

    $effect(() => {
        if ($trafficHistory.length >= 0) buildCalendar();
    });
</script>

<div class="page">
    <h2>Traffic History</h2>

    <div class="today-card">
        <span class="today-label">Today</span>
        <span class="today-val">
            ↓ {formatTrafficBytes($trafficToday.downBytes)}
            &nbsp; ↑ {formatTrafficBytes($trafficToday.upBytes)}
            &nbsp;
            <span class="today-total"
                >∑ {formatTrafficBytes(
                    $trafficToday.downBytes + $trafficToday.upBytes,
                )}</span
            >
        </span>
    </div>

    <div class="month-nav">
        <button class="nav-btn" onclick={prevMonth}>‹</button>
        <span class="month-label">{monthLabel}</span>
        <button class="nav-btn" onclick={nextMonth}>›</button>
    </div>

    <div class="filter-bar">
        {#each availableTypes as t}
            <button
                class="filter-btn"
                class:active={activeFilter === t}
                onclick={() => setFilter(t)}
            >
                {t === "all" ? "All" : t}
            </button>
        {/each}
    </div>

    <div class="month-totals">
        <span class="total-label">↓ {formatTrafficBytes(totalDown)}</span>
        <span class="total-label">↑ {formatTrafficBytes(totalUp)}</span>
        <span class="total-label total-combined"
            >∑ {formatTrafficBytes(totalDown + totalUp)}</span
        >
    </div>

    <div class="legend">
        <span class="legend-item"
            ><span class="legend-swatch tier-blue"></span> ≤10 GB</span
        >
        <span class="legend-item"
            ><span class="legend-swatch tier-low"></span> ≤50 GB</span
        >
        <span class="legend-item"
            ><span class="legend-swatch tier-med"></span> ≤100 GB</span
        >
        <span class="legend-item"
            ><span class="legend-swatch tier-orange"></span> ≤150 GB</span
        >
        <span class="legend-item"
            ><span class="legend-swatch tier-high"></span> ≤200 GB</span
        >
        <span class="legend-item"
            ><span class="legend-swatch tier-max"></span> &gt;200 GB</span
        >
    </div>

    <div class="calendar-grid">
        <div class="day-header">Mon</div>
        <div class="day-header">Tue</div>
        <div class="day-header">Wed</div>
        <div class="day-header">Thu</div>
        <div class="day-header">Fri</div>
        <div class="day-header">Sat</div>
        <div class="day-header sun">Sun</div>

        {#each calendarDays as day}
            {#if day}
                <button
                    class="day-cell {getTierClass(day.totalBytes)}"
                    class:selected={selectedDay?.day === day.day}
                    onclick={() =>
                        (selectedDay =
                            selectedDay?.day === day.day ? null : day)}
                    title={`${day.date}\n↓ ${formatTrafficBytes(day.downBytes)}\n↑ ${formatTrafficBytes(day.upBytes)}\n∑ ${formatTrafficBytes(day.totalBytes)}`}
                >
                    <span class="day-num">{day.day}</span>
                    {#if day.totalBytes > 0}
                        <span class="cell-total"
                            >{formatTrafficBytes(day.totalBytes)}</span
                        >
                    {/if}
                </button>
            {:else}
                <div class="day-cell empty"></div>
            {/if}
        {/each}
    </div>

    {#if selectedDay}
        <div class="day-detail">
            <h3>{selectedDay.date}</h3>
            <div class="detail-row">
                <span class="detail-label">Download</span>
                <span class="detail-val down"
                    >{formatTrafficBytes(selectedDay.downBytes)}</span
                >
            </div>
            <div class="detail-row">
                <span class="detail-label">Upload</span>
                <span class="detail-val up"
                    >{formatTrafficBytes(selectedDay.upBytes)}</span
                >
            </div>
            <div class="detail-row">
                <span class="detail-label">Combined</span>
                <span class="detail-val total"
                    >{formatTrafficBytes(selectedDay.totalBytes)}</span
                >
            </div>
            {#if selectedDay.records?.length}
                <div class="detail-breakdown">
                    <span class="detail-label">Breakdown</span>
                    {#each selectedDay.records as rec}
                        <div class="breakdown-row">
                            <span class="breakdown-iface"
                                >{rec.interfaceType ||
                                    rec.interface ||
                                    "?"}</span
                            >
                            <span class="breakdown-val"
                                >↓{formatTrafficBytes(rec.downBytes)} ↑{formatTrafficBytes(
                                    rec.upBytes,
                                )}</span
                            >
                        </div>
                    {/each}
                </div>
            {/if}
        </div>
    {/if}

    <div class="import-section">
        <h3>Import</h3>
        <input
            type="file"
            accept=".dat"
            class="file-input"
            id="dat-file-input"
            onchange={(e) => {
                const file = e.target?.files?.[0];
                if (!file) return;
                const reader = new FileReader();
                reader.onload = () => {
                    importDatContent(reader.result);
                };
                reader.readAsText(file);
                e.target.value = "";
            }}
        />
        <label for="dat-file-input" class="file-label">Select .dat file</label>
        {#if $importResult}
            <span class="import-status" class:error={$importResult.error}>
                {#if $importResult.error}
                    {$importResult.error}
                {:else}
                    Imported {$importResult.daysImported} day(s)
                {/if}
            </span>
        {/if}
    </div>
</div>

<style>
    .page {
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
    }
    h2 {
        font-size: var(--text-lg);
        font-weight: 600;
        margin: 0;
    }

    .today-card {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        background: var(--color-surface);
        border-radius: var(--radius-md);
        border: 1px solid var(--color-border);
    }
    .today-label {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        font-weight: 500;
    }
    .today-val {
        font-size: var(--text-sm);
        font-weight: 600;
        color: var(--color-primary);
    }
    .today-total {
        color: var(--color-text);
    }

    .month-nav {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-4);
        flex-wrap: wrap;
    }
    .nav-btn {
        padding: var(--space-1) var(--space-3);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        background: var(--color-surface);
        color: var(--color-text);
        font-size: var(--text-lg);
        cursor: pointer;
        transition: background var(--transition);
    }
    .nav-btn:hover {
        background: var(--color-surface-dynamic);
    }
    .month-label {
        font-size: var(--text-base);
        font-weight: 600;
        min-width: 120px;
        text-align: center;
    }

    .filter-bar {
        display: flex;
        gap: var(--space-2);
        justify-content: center;
        flex-wrap: wrap;
    }
    .filter-btn {
        padding: var(--space-1) var(--space-3);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-full);
        background: var(--color-bg);
        color: var(--color-text-muted);
        font-size: var(--text-xs);
        cursor: pointer;
        font-weight: 500;
        transition: all var(--transition);
    }
    .filter-btn:hover {
        border-color: var(--color-primary);
        color: var(--color-text);
    }
    .filter-btn.active {
        background: var(--color-primary);
        color: white;
        border-color: var(--color-primary);
    }

    .month-totals {
        display: flex;
        gap: var(--space-4);
        justify-content: center;
        flex-wrap: wrap;
    }
    .total-label {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
    }
    .total-combined {
        color: var(--color-text);
        font-weight: 600;
    }

    .legend {
        display: flex;
        gap: var(--space-4);
        justify-content: center;
        flex-wrap: wrap;
    }
    .legend-item {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--text-xs);
        color: var(--color-text-muted);
    }
    .legend-swatch {
        width: 10px;
        height: 10px;
        border-radius: 2px;
        flex-shrink: 0;
    }
    .legend-swatch.tier-blue {
        background: #00b7ee;
    }
    .legend-swatch.tier-low {
        background: #80c269;
    }
    .legend-swatch.tier-med {
        background: #ffd83a;
    }
    .legend-swatch.tier-orange {
        background: #ff8844;
    }
    .legend-swatch.tier-high {
        background: #ff5f4a;
    }
    .legend-swatch.tier-max {
        background: #a61300;
    }

    .calendar-grid {
        display: grid;
        grid-template-columns: repeat(7, 1fr);
        gap: 2px;
    }
    .day-header {
        text-align: center;
        font-size: var(--text-xs);
        color: var(--color-text-muted);
        padding: var(--space-1);
    }
    .day-header.sun {
        color: #ef4444;
    }
    .day-cell {
        position: relative;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        min-height: 48px;
        padding: var(--space-1);
        border-radius: var(--radius-sm);
        border: 1px solid var(--color-border);
        background: var(--color-bg);
        cursor: pointer;
        transition: all var(--transition);
        overflow: hidden;
    }
    .day-cell:hover {
        border-color: var(--color-primary);
        background: var(--color-surface-dynamic);
    }
    .day-cell.selected {
        border-color: var(--color-primary);
        background: var(--color-primary-hl);
    }
    .day-cell.empty {
        border-color: transparent;
        background: transparent;
        cursor: default;
    }
    .day-cell.tier-blue {
        background: #00b7ee18;
        border-color: #00b7ee;
    }
    .day-cell.tier-low {
        background: #80c26918;
        border-color: #80c269;
    }
    .day-cell.tier-med {
        background: #ffd83a18;
        border-color: #ffd83a;
    }
    .day-cell.tier-orange {
        background: #ff884418;
        border-color: #ff8844;
    }
    .day-cell.tier-high {
        background: #ff5f4a18;
        border-color: #ff5f4a;
    }
    .day-cell.tier-max {
        background: #a6130018;
        border-color: #a61300;
    }

    .day-num {
        font-size: var(--text-xs);
        font-weight: 600;
        color: var(--color-text);
    }
    .cell-total {
        font-size: 10px;
        font-weight: 700;
        color: var(--color-text);
        line-height: 1;
        margin-top: 2px;
    }

    .day-detail {
        padding: var(--space-3) var(--space-4);
        background: var(--color-surface);
        border-radius: var(--radius-md);
        border: 1px solid var(--color-border);
    }
    .day-detail h3 {
        font-size: var(--text-sm);
        font-weight: 600;
        margin-bottom: var(--space-2);
    }
    .detail-row {
        display: flex;
        justify-content: space-between;
        padding: var(--space-1) 0;
    }
    .detail-label {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
    }
    .detail-val {
        font-size: var(--text-sm);
        font-weight: 500;
    }
    .detail-val.down {
        color: var(--color-primary);
    }
    .detail-val.up {
        color: var(--color-accent);
    }
    .detail-val.total {
        color: var(--color-text);
        font-weight: 600;
    }

    .detail-breakdown {
        margin-top: var(--space-2);
        padding-top: var(--space-2);
        border-top: 1px solid var(--color-border);
    }
    .breakdown-row {
        display: flex;
        justify-content: space-between;
        padding: 2px 0;
        font-size: var(--text-xs);
    }
    .breakdown-iface {
        color: var(--color-text-muted);
    }
    .breakdown-val {
        color: var(--color-text);
        font-weight: 500;
    }

    .import-section {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        background: var(--color-surface);
        border-radius: var(--radius-md);
        border: 1px solid var(--color-border);
    }
    .import-section h3 {
        font-size: var(--text-sm);
        font-weight: 600;
        margin: 0;
        flex-shrink: 0;
    }
    .file-input {
        display: none;
    }
    .file-label {
        padding: var(--space-1) var(--space-3);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        background: var(--color-bg);
        color: var(--color-text);
        font-size: var(--text-xs);
        cursor: pointer;
        transition: all var(--transition);
        flex-shrink: 0;
    }
    .file-label:hover {
        border-color: var(--color-primary);
        background: var(--color-surface-dynamic);
    }
    .import-status {
        font-size: var(--text-xs);
        color: var(--color-success);
    }
    .import-status.error {
        color: var(--color-danger);
    }
</style>

<script>
    import { onMount } from "svelte";
    import {
        allTrafficHistory,
        trafficHistory,
        trafficToday,
        importResult,
        windowsImportResult,
    } from "../../lib/store.ts";
    import { formatTrafficBytes, formatDate } from "../../lib/utils.ts";
    import {
        importDatContent,
        importWindowsUsage,
        requestTrafficHistoryAll,
    } from "../../lib/bridge.ts";

    onMount(() => {
        if ($allTrafficHistory.length === 0) {
            requestTrafficHistoryAll();
        }
    });

    function formatDateInput(date) {
        return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
    }

    const today = new Date();
    const sevenDaysAgo = new Date();
    sevenDaysAgo.setDate(sevenDaysAgo.getDate() - 7);

    let winStartDate = $state(formatDateInput(sevenDaysAgo));
    let winEndDate = $state(formatDateInput(today));
    let isWindowsImporting = $state(false);
    let dismissTimer = null;

    function toDmy(isoStr) {
        if (!isoStr) return "";
        const parts = isoStr.split("-");
        if (parts.length === 3 && parts[0].length === 4) {
            return `${parts[2]}-${parts[1]}-${parts[0]}`;
        }
        return isoStr;
    }

    function validateDateString(str) {
        if (!str || !str.trim()) return "Date is required.";
        if (!/^\d{4}-\d{2}-\d{2}$/.test(str))
            return "Invalid date (use DD-MM-YYYY).";
        const [y, m, d] = str.split("-").map(Number);
        if (y < 2000 || y > 2100 || m < 1 || m > 12 || d < 1 || d > 31)
            return "Invalid date.";
        const dateObj = new Date(y, m - 1, d);
        if (
            dateObj.getFullYear() !== y ||
            dateObj.getMonth() !== m - 1 ||
            dateObj.getDate() !== d
        ) {
            return "Invalid calendar date.";
        }
        return null;
    }

    let startDateError = $derived.by(() => {
        const err = validateDateString(winStartDate);
        if (err) return err;
        const todayStr = formatDateInput(new Date());
        if (winStartDate > todayStr)
            return "Start date cannot be in the future.";
        return null;
    });

    let endDateError = $derived.by(() => {
        const err = validateDateString(winEndDate);
        if (err) return err;
        const todayStr = formatDateInput(new Date());
        if (winEndDate > todayStr) return "End date cannot be in the future.";
        return null;
    });

    let dateRangeError = $derived.by(() => {
        if (startDateError) return startDateError;
        if (endDateError) return endDateError;
        if (winStartDate > winEndDate)
            return "Start date cannot be after End date.";
        return null;
    });

    function setPreset(days) {
        const end = new Date();
        const start = new Date();
        start.setDate(start.getDate() - days);
        winStartDate = formatDateInput(start);
        winEndDate = formatDateInput(end);
        windowsImportResult.set(null);
    }

    function handleWindowsImport() {
        if (dateRangeError || isWindowsImporting) return;
        isWindowsImporting = true;
        windowsImportResult.set(null);
        importWindowsUsage(toDmy(winStartDate), toDmy(winEndDate));
    }

    $effect(() => {
        if ($windowsImportResult) {
            isWindowsImporting = false;
            if (dismissTimer) clearTimeout(dismissTimer);
            dismissTimer = setTimeout(() => {
                windowsImportResult.set(null);
            }, 6000);
        }
    });

    const _initDate = new Date();
    let currentYear = $state(_initDate.getFullYear());
    let currentMonth = $state(_initDate.getMonth() + 1);
    let selectedDay = $state(null);
    let activeFilter = $state("all");

    let monthRecords = $derived(
        $allTrafficHistory.filter((r) => {
            const p = r.date.split("-");
            return (
                p.length === 3 &&
                parseInt(p[2]) === currentYear &&
                parseInt(p[1]) === currentMonth
            );
        }),
    );

    $effect(() => {
        trafficHistory.set(monthRecords);
    });

    let availableTypes = $derived([
        "all",
        ...new Set(monthRecords.map((r) => r.interfaceName).filter(Boolean)),
    ]);

    let filteredRecords = $derived(
        activeFilter === "all"
            ? monthRecords
            : monthRecords.filter((r) => r.interfaceName === activeFilter),
    );

    let totalDown = $derived(
        filteredRecords.reduce((s, r) => s + r.downBytes, 0),
    );
    let totalUp = $derived(filteredRecords.reduce((s, r) => s + r.upBytes, 0));

    let monthLabel = $derived(
        new Date(currentYear, currentMonth - 1, 1).toLocaleString("default", {
            year: "numeric",
            month: "long",
        }),
    );

    function prevMonth() {
        selectedDay = null;
        if (currentMonth === 1) {
            currentMonth = 12;
            currentYear--;
        } else currentMonth--;
    }

    function nextMonth() {
        selectedDay = null;
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

    function setFilter(type) {
        activeFilter = type;
        selectedDay = null;
    }

    let calendarDays = $derived.by(() => {
        const records = filteredRecords;
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
                source: r.source,
                downBytes: r.downBytes,
                upBytes: r.upBytes,
            });
        }

        const d = new Date(currentYear, currentMonth - 1, 1).getDay();
        const firstCol = d === 0 ? 6 : d - 1;

        const days = [];
        for (let i = 0; i < firstCol; i++) days.push(null);

        const now = new Date();
        for (let day = 1; day <= dim; day++) {
            const dateStr = `${String(day).padStart(2, "0")}-${String(currentMonth).padStart(2, "0")}-${currentYear}`;
            const group = dateGroups[dateStr];
            const down = group ? group.downBytes : 0;
            const up = group ? group.upBytes : 0;
            const total = down + up;

            const isToday =
                day === now.getDate() &&
                currentMonth === now.getMonth() + 1 &&
                currentYear === now.getFullYear();

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
        return days;
    });
</script>

<div class="page">
    <h2>Traffic History Calendar</h2>

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
                    title={`${formatDate(day.date)}\n↓ ${formatTrafficBytes(day.downBytes)}\n↑ ${formatTrafficBytes(day.upBytes)}\n∑ ${formatTrafficBytes(day.totalBytes)}`}
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
            <h3>{formatDate(selectedDay.date)}</h3>
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
                                    "?"}{#if rec.source && rec.source !== "LocalTelemetry"}<span
                                        class="source-badge"
                                        class:win-source={rec.source ===
                                            "WindowsImport"}
                                        class:dat-source={rec.source ===
                                            "DatImport"}
                                        >{rec.source === "WindowsImport"
                                            ? "Windows"
                                            : rec.source === "DatImport"
                                              ? ".DAT Import"
                                              : rec.source}</span
                                    >{/if}</span
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
        <h3>Import History</h3>

        <div class="import-card">
            <div class="card-header">
                <span class="card-title">Windows Network Usage</span>
                <span class="card-desc"
                    >Import historical usage records tracked by Windows for
                    missed periods</span
                >
            </div>

            <div class="preset-row">
                <span class="preset-label">Presets:</span>
                <button
                    type="button"
                    class="preset-btn"
                    onclick={() => setPreset(7)}>Last 7 Days</button
                >
                <button
                    type="button"
                    class="preset-btn"
                    onclick={() => setPreset(14)}>Last 14 Days</button
                >
                <button
                    type="button"
                    class="preset-btn"
                    onclick={() => setPreset(30)}>Last 30 Days</button
                >
            </div>

            <div class="date-picker-row">
                <div class="date-field">
                    <label for="win-start-date">From</label>
                    <input
                        id="win-start-date"
                        type="date"
                        class="date-input"
                        class:invalid={!!startDateError}
                        bind:value={winStartDate}
                        oninput={() => windowsImportResult.set(null)}
                        max={formatDateInput(new Date())}
                    />
                </div>
                <div class="date-field">
                    <label for="win-end-date">To</label>
                    <input
                        id="win-end-date"
                        type="date"
                        class="date-input"
                        class:invalid={!!endDateError}
                        bind:value={winEndDate}
                        oninput={() => windowsImportResult.set(null)}
                        max={formatDateInput(new Date())}
                    />
                </div>
                <button
                    type="button"
                    class="action-btn"
                    disabled={isWindowsImporting || !!dateRangeError}
                    onclick={handleWindowsImport}
                >
                    {isWindowsImporting
                        ? "Importing..."
                        : "Import from Windows"}
                </button>
            </div>

            {#if dateRangeError}
                <div class="validation-error">{dateRangeError}</div>
            {/if}

            {#if $windowsImportResult}
                <div
                    class="import-status"
                    class:error={!$windowsImportResult.success}
                >
                    {#if $windowsImportResult.success}
                        {#if $windowsImportResult.daysImported > 0}
                            Imported {$windowsImportResult.daysImported} day(s):
                            ↓ {formatTrafficBytes(
                                $windowsImportResult.downBytes,
                            )}, ↑ {formatTrafficBytes(
                                $windowsImportResult.upBytes,
                            )}
                        {:else}
                            {$windowsImportResult.message ||
                                "Selected date range is already up to date in LocalTelemetry."}
                        {/if}
                    {:else}
                        {$windowsImportResult.message ||
                            "Failed to import Windows usage."}
                    {/if}
                </div>
            {/if}
        </div>

        <div class="import-card">
            <div class="card-header">
                <span class="card-title">Traffic Database & Backup</span>
                <span class="card-desc"
                    >Import backup files (.jsonl or legacy .dat format)</span
                >
            </div>
            <div class="dat-row">
                <input
                    type="file"
                    accept=".jsonl,.dat"
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
                <label for="dat-file-input" class="file-label"
                    >Select .jsonl or .dat file</label
                >
                {#if $importResult}
                    <span
                        class="import-status"
                        class:error={$importResult.error}
                    >
                        {#if $importResult.error}
                            {$importResult.error}
                        {:else}
                            Imported {$importResult.daysImported} day(s)
                        {/if}
                    </span>
                {/if}
            </div>
        </div>
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
        display: inline-flex;
        align-items: center;
        gap: 6px;
    }
    .source-badge {
        font-size: 10px;
        font-weight: 600;
        padding: 1px 5px;
        border-radius: var(--radius-sm, 3px);
        line-height: 1.2;
        text-transform: uppercase;
        letter-spacing: 0.03em;
        background: var(--color-surface-dynamic, #2d2c2a);
        color: var(--color-text-muted, #797876);
        border: 1px solid var(--color-border, #393836);
    }
    .source-badge.win-source {
        background: rgba(2, 132, 199, 0.15);
        color: #38bdf8;
        border-color: rgba(56, 189, 248, 0.3);
    }
    .source-badge.dat-source {
        background: rgba(234, 148, 48, 0.15);
        color: #fdab43;
        border-color: rgba(253, 171, 67, 0.3);
    }
    .breakdown-val {
        color: var(--color-text);
        font-weight: 500;
    }

    .import-section {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
        margin-top: var(--space-2);
    }
    .import-section h3 {
        font-size: var(--text-sm);
        font-weight: 600;
        margin: 0;
    }
    .import-card {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        background: var(--color-surface);
        border-radius: var(--radius-md);
        border: 1px solid var(--color-border);
    }
    .card-header {
        display: flex;
        flex-direction: column;
        gap: 2px;
    }
    .card-title {
        font-size: var(--text-sm);
        font-weight: 600;
        color: var(--color-text);
    }
    .card-desc {
        font-size: var(--text-xs);
        color: var(--color-text-muted);
    }
    .preset-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        flex-wrap: wrap;
    }
    .preset-label {
        font-size: var(--text-xs);
        color: var(--color-text-muted);
    }
    .preset-btn {
        padding: 2px 8px;
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        color: var(--color-text);
        font-size: var(--text-xs);
        cursor: pointer;
        transition: all var(--transition);
    }
    .preset-btn:hover {
        border-color: var(--color-primary);
        background: var(--color-surface-dynamic);
    }
    .date-picker-row {
        display: flex;
        align-items: flex-end;
        gap: var(--space-3);
        flex-wrap: wrap;
    }
    .date-field {
        display: flex;
        flex-direction: column;
        gap: 4px;
    }
    .date-field label {
        font-size: var(--text-xs);
        color: var(--color-text-muted);
    }
    .date-input {
        padding: var(--space-1) var(--space-2);
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        color: var(--color-text);
        font-size: var(--text-xs);
        outline: none;
        transition: border-color var(--transition);
    }
    .date-input:focus {
        border-color: var(--color-primary);
    }
    .date-input.invalid {
        border-color: var(--color-danger);
    }
    .validation-error {
        font-size: var(--text-xs);
        color: var(--color-danger);
    }
    .action-btn {
        padding: 6px var(--space-3);
        background: var(--color-primary);
        color: #ffffff;
        border: none;
        border-radius: var(--radius-sm);
        font-size: var(--text-xs);
        font-weight: 500;
        cursor: pointer;
        transition: opacity var(--transition);
    }
    .action-btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }
    .action-btn:not(:disabled):hover {
        opacity: 0.9;
    }
    .dat-row {
        display: flex;
        align-items: center;
        gap: var(--space-3);
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

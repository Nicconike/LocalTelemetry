<script lang="ts">
    import { onMount } from "svelte";
    import { systemInfo, settings } from "../../lib/store.ts";
    import {
        openUrl,
        openFolder,
        checkForUpdates,
        onMessage,
    } from "../../lib/bridge.ts";
    import type { UpdateCheckResultDto } from "../../lib/types.ts";
    import Select from "../ui/Select.svelte";
    import Toggle from "../ui/Toggle.svelte";

    let updateStatus = $state<
        "idle" | "checking" | "upToDate" | "updateAvailable" | "error"
    >("idle");
    let updateResult = $state<UpdateCheckResultDto | null>(null);

    const freqOptions = [
        { value: "weekly", label: "Weekly (Every 7 days)" },
        { value: "monthly", label: "Monthly (Every 30 days)" },
        { value: "manual", label: "Manual (Check on demand)" },
    ];

    let checkTimeoutTimer: ReturnType<typeof setTimeout> | null = null;
    let autoResetTimer: ReturnType<typeof setTimeout> | null = null;

    onMount(() => {
        const cleanup = onMessage("updateCheckResult", (payload) => {
            if (checkTimeoutTimer) {
                clearTimeout(checkTimeoutTimer);
                checkTimeoutTimer = null;
            }
            if (autoResetTimer) {
                clearTimeout(autoResetTimer);
                autoResetTimer = null;
            }
            const res = payload as UpdateCheckResultDto & {
                IsUpdateAvailable?: boolean;
                IsPrerelease?: boolean;
                LatestVersion?: string;
                ErrorMessage?: string;
            };
            const isAvailable =
                res.isUpdateAvailable ?? res.IsUpdateAvailable ?? false;
            const errMsg = res.errorMessage ?? res.ErrorMessage;
            updateResult = {
                isUpdateAvailable: isAvailable,
                isPrerelease: res.isPrerelease ?? res.IsPrerelease ?? false,
                currentVersion: res.currentVersion ?? "",
                latestVersion: res.latestVersion ?? res.LatestVersion ?? "",
                releaseUrl: res.releaseUrl ?? "",
                releaseNotes: res.releaseNotes ?? "",
                publishedAt: res.publishedAt ?? "",
                errorMessage: errMsg,
            };

            if (errMsg) {
                updateStatus = "error";
                autoResetTimer = setTimeout(() => {
                    if (updateStatus === "error") updateStatus = "idle";
                }, 3500);
            } else if (isAvailable) {
                updateStatus = "updateAvailable";
            } else {
                updateStatus = "upToDate";
                autoResetTimer = setTimeout(() => {
                    if (updateStatus === "upToDate") updateStatus = "idle";
                }, 3500);
            }
        });
        return () => {
            if (checkTimeoutTimer) clearTimeout(checkTimeoutTimer);
            if (autoResetTimer) clearTimeout(autoResetTimer);
            cleanup();
        };
    });

    function handleCheckForUpdates() {
        if (checkTimeoutTimer) clearTimeout(checkTimeoutTimer);
        if (autoResetTimer) clearTimeout(autoResetTimer);
        updateStatus = "checking";
        checkForUpdates(true);
        checkTimeoutTimer = setTimeout(() => {
            if (updateStatus === "checking") {
                updateStatus = "error";
                updateResult = {
                    isUpdateAvailable: false,
                    isPrerelease: false,
                    currentVersion: $systemInfo.version ?? "",
                    latestVersion: "",
                    releaseUrl: "",
                    releaseNotes: "",
                    publishedAt: "",
                    errorMessage: "Request timed out",
                };
                autoResetTimer = setTimeout(() => {
                    if (updateStatus === "error") updateStatus = "idle";
                }, 3500);
            }
        }, 10000);
    }

    const links = [
        {
            label: "Report Issue",
            url: "https://github.com/Nicconike/LocalTelemetry/issues",
            svg: `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="1.8"/><path d="M12 8v4M12 16h.01" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>`,
        },
        {
            label: "Releases",
            url: "https://github.com/Nicconike/LocalTelemetry/releases",
            svg: `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>`,
        },
        {
            label: "Documentation",
            url: "https://nicconike.github.io/LocalTelemetry/",
            svg: `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>`,
        },
        {
            label: "License",
            url: "https://github.com/Nicconike/LocalTelemetry/blob/master/LICENSE",
            svg: `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>`,
        },
    ];

    const licenses = [
        { name: ".NET Runtime", type: "MIT" },
        { name: "Svelte 5", type: "MIT" },
        { name: "Vite", type: "MIT" },
        { name: "Microsoft WebView2", type: "Proprietary" },
        { name: "PawnIo Driver", type: "GPL-2.0" },
        { name: "MinVer", type: "Apache-2.0" },
    ];

    // Normalize redundant trailing revision/build zeros from assembly versions
    // (e.g. 0.1.0.0 -> 0.1.0) but never mangle a SemVer string such as 1.0.0-rc.1.
    const cleanVersion = $derived.by(() => {
        const raw = $systemInfo.version;
        if (!raw) return "-";
        const parts = raw.split(".");
        const isNumeric = parts.every((p) => /^\d+$/.test(p));
        if (isNumeric && parts.length === 4 && parts[3] === "0") {
            if (parts[2] === "0") {
                return `${parts[0]}.${parts[1]}`;
            }
            return `${parts[0]}.${parts[1]}.${parts[2]}`;
        }
        return raw;
    });

    // Format build date as DD-MM-YYYY with 12-hour format (hh:mm AM/PM)
    const formattedBuildDate = $derived.by(() => {
        const raw = $systemInfo.buildDate;
        if (!raw) return "-";
        // Handle "dd-MM-yyyy hh:mm tt", "dd-MM-yyyy, hh:mm tt", "yyyy-MM-dd HH:mm" or general formats
        const dmyMatch = raw.match(/^(\d{1,2})[-/\s](\d{1,2})[-/\s](\d{4})(?:[,\s]+(\d{1,2}):(\d{2})(?::\d{2})?(?:\s*(AM|PM))?)?/i);
        if (dmyMatch) {
            const d = dmyMatch[1].padStart(2, "0");
            const m = dmyMatch[2].padStart(2, "0");
            const y = dmyMatch[3];
            if (dmyMatch[4] && dmyMatch[5]) {
                let hours = parseInt(dmyMatch[4], 10);
                const minutes = dmyMatch[5];
                let ampm = dmyMatch[6]?.toUpperCase();
                if (!ampm) {
                    ampm = hours >= 12 ? "PM" : "AM";
                    hours = hours % 12 || 12;
                }
                const hStr = String(hours).padStart(2, "0");
                return `${d}-${m}-${y}, ${hStr}:${minutes} ${ampm}`;
            }
            return `${d}-${m}-${y}`;
        }
        const ymdMatch = raw.match(/^(\d{4})[-/\s](\d{1,2})[-/\s](\d{1,2})(?:[,\s]+(\d{1,2}):(\d{2})(?::\d{2})?(?:\s*(AM|PM))?)?/i);
        if (ymdMatch) {
            const y = ymdMatch[1];
            const m = ymdMatch[2].padStart(2, "0");
            const d = ymdMatch[3].padStart(2, "0");
            if (ymdMatch[4] && ymdMatch[5]) {
                let hours = parseInt(ymdMatch[4], 10);
                const minutes = ymdMatch[5];
                let ampm = ymdMatch[6]?.toUpperCase();
                if (!ampm) {
                    ampm = hours >= 12 ? "PM" : "AM";
                    hours = hours % 12 || 12;
                }
                const hStr = String(hours).padStart(2, "0");
                return `${d}-${m}-${y}, ${hStr}:${minutes} ${ampm}`;
            }
            return `${d}-${m}-${y}`;
        }
        return raw;
    });
</script>

<div class="page-container">
    <!-- Brand Header -->
    <div class="brand-hero">
        <img src="app.ico" alt="LocalTelemetry Icon" class="app-icon" />
        <div class="brand-info">
            <h1 class="brand-title">LocalTelemetry</h1>
            <p class="brand-tagline">
                Real-time hardware telemetry displayed on the Windows taskbar
            </p>
        </div>
    </div>

    <!-- Software Updates Section -->
    <div class="info-card updates-card">
        <h3>Software Updates</h3>
        <div class="update-card-content">
            <div class="update-status-row">
                <span class="update-label">Status</span>
                <div class="update-action-box">
                    {#if updateStatus === "idle"}
                        <button
                            class="update-btn"
                            onclick={handleCheckForUpdates}
                        >
                            Check for Updates
                        </button>
                    {:else if updateStatus === "checking"}
                        <span class="update-badge checking"> Checking... </span>
                    {:else if updateStatus === "upToDate"}
                        <span class="update-badge up-to-date">
                            <svg
                                class="badge-icon"
                                width="13"
                                height="13"
                                viewBox="0 0 24 24"
                                fill="none"
                                ><path
                                    d="M20 6L9 17l-5-5"
                                    stroke="currentColor"
                                    stroke-width="2.2"
                                    stroke-linecap="round"
                                    stroke-linejoin="round"
                                /></svg
                            >
                            Up to date
                        </span>
                    {:else if updateStatus === "updateAvailable"}
                        <div class="update-found-box">
                            <span class="update-badge available">
                                <svg
                                    class="badge-icon"
                                    width="13"
                                    height="13"
                                    viewBox="0 0 24 24"
                                    fill="none"
                                    ><path
                                        d="M12 3v12m0 0l-4-4m4 4l4-4M4 21h16"
                                        stroke="currentColor"
                                        stroke-width="2"
                                        stroke-linecap="round"
                                        stroke-linejoin="round"
                                    /></svg
                                >
                                {updateResult?.latestVersion?.startsWith("v")
                                    ? ""
                                    : "v"}{updateResult?.latestVersion} Available
                            </span>
                            <button
                                class="download-btn"
                                onclick={() =>
                                    openUrl(
                                        updateResult?.releaseUrl ||
                                            "https://github.com/Nicconike/LocalTelemetry/releases",
                                    )}
                            >
                                Download
                            </button>
                        </div>
                    {:else if updateStatus === "error"}
                        <span
                            class="update-badge error"
                            title={updateResult?.errorMessage || "Check failed"}
                        >
                            <svg
                                class="badge-icon"
                                width="13"
                                height="13"
                                viewBox="0 0 24 24"
                                fill="none"
                                ><circle
                                    cx="12"
                                    cy="12"
                                    r="10"
                                    stroke="currentColor"
                                    stroke-width="1.8"
                                /><path
                                    d="M12 8v4M12 16h.01"
                                    stroke="currentColor"
                                    stroke-width="2"
                                    stroke-linecap="round"
                                /></svg
                            >
                            {updateResult?.errorMessage || "Check failed"}
                        </span>
                    {/if}
                </div>
            </div>

            {#if $settings?.updates}
                <div class="update-settings-sub">
                    <Select
                        label="Check Frequency"
                        options={freqOptions}
                        bind:value={$settings.updates.checkFrequency}
                        inline
                    />
                    <Toggle
                        bind:checked={$settings.updates.includePrereleases}
                        label="Include pre-release & rc builds"
                    />
                </div>
            {/if}
        </div>
    </div>

    <!-- Quick Info Cards -->
    <div class="grid-section">
        <div class="info-card">
            <h3>Application Metadata</h3>
            <div class="specs-list">
                <div class="spec-item">
                    <span class="spec-label">Version</span>
                    <span class="spec-value highlight">v{cleanVersion}</span>
                </div>
                <div class="spec-item">
                    <span class="spec-label">Deployment Mode</span>
                    <span class="spec-value"
                        >{$systemInfo.deploymentMode ?? "Standard"}</span
                    >
                </div>
                <div class="spec-item">
                    <span class="spec-label">Build Date</span>
                    <span class="spec-value"
                        >{formattedBuildDate}</span
                    >
                </div>
                <div class="spec-item">
                    <span class="spec-label">Target Runtime</span>
                    <span class="spec-value"
                        >{$systemInfo.targetRuntime ?? ".NET 10.0"}</span
                    >
                </div>
                <div class="spec-item">
                    <span class="spec-label">Platform</span>
                    <span class="spec-value">Windows (x64)</span>
                </div>
            </div>
        </div>

        <div class="info-card">
            <h3>Resources & Links</h3>
            <div class="links-stack">
                {#each links as link}
                    <button class="link-item" onclick={() => openUrl(link.url)}>
                        <span class="link-icon">{@html link.svg}</span>
                        <span class="link-label">{link.label}</span>
                        <span class="arrow-indicator">→</span>
                    </button>
                {/each}
            </div>
        </div>
    </div>

    <!-- Storage & Directories Section -->
    <div class="info-card">
        <h3>Application Directories</h3>
        <div class="links-stack">
            {#if $systemInfo?.isStandardMode}
                <button class="link-item" onclick={() => openFolder("app")}>
                    <span class="link-icon">
                        <svg
                            width="15"
                            height="15"
                            viewBox="0 0 24 24"
                            fill="none"
                            ><path
                                d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z"
                                stroke="currentColor"
                                stroke-width="1.8"
                                stroke-linecap="round"
                                stroke-linejoin="round"
                            /></svg
                        >
                    </span>
                    <span class="link-label">Installation Folder</span>
                    <span
                        class="link-sub-path"
                        title={$systemInfo.exeDir ?? "Program Files"}
                        >{$systemInfo.exeDir ??
                            "C:\\Program Files\\LocalTelemetry"}</span
                    >
                    <span class="arrow-indicator">→</span>
                </button>
                <button class="link-item" onclick={() => openFolder("data")}>
                    <span class="link-icon">
                        <svg
                            width="15"
                            height="15"
                            viewBox="0 0 24 24"
                            fill="none"
                            ><path
                                d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10"
                                stroke="currentColor"
                                stroke-width="1.8"
                                stroke-linecap="round"
                                stroke-linejoin="round"
                            /></svg
                        >
                    </span>
                    <span class="link-label">Settings & Data Folder</span>
                    <span
                        class="link-sub-path"
                        title={$systemInfo.dataDir ?? "AppData"}
                        >{$systemInfo.dataDir ??
                            "%LocalAppData%\\LocalTelemetry"}</span
                    >
                    <span class="arrow-indicator">→</span>
                </button>
                <button class="link-item" onclick={() => openFolder("logs")}>
                    <span class="link-icon">
                        <svg
                            width="15"
                            height="15"
                            viewBox="0 0 24 24"
                            fill="none"
                            ><path
                                d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
                                stroke="currentColor"
                                stroke-width="1.8"
                                stroke-linecap="round"
                                stroke-linejoin="round"
                            /></svg
                        >
                    </span>
                    <span class="link-label">Logs Folder</span>
                    <span
                        class="link-sub-path"
                        title={$systemInfo.logsDir ?? "Logs"}
                        >{$systemInfo.logsDir ??
                            "%LocalAppData%\\LocalTelemetry\\logs"}</span
                    >
                    <span class="arrow-indicator">→</span>
                </button>
            {:else}
                <button class="link-item" onclick={() => openFolder("app")}>
                    <span class="link-icon">
                        <svg
                            width="15"
                            height="15"
                            viewBox="0 0 24 24"
                            fill="none"
                            ><path
                                d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z"
                                stroke="currentColor"
                                stroke-width="1.8"
                                stroke-linecap="round"
                                stroke-linejoin="round"
                            /></svg
                        >
                    </span>
                    <span class="link-label">Portable Application Folder</span>
                    <span
                        class="link-sub-path"
                        title={$systemInfo?.exeDir ?? "Application Directory"}
                        >{$systemInfo?.exeDir ??
                            "Executable & Data Directory"}</span
                    >
                    <span class="arrow-indicator">→</span>
                </button>
                <button class="link-item" onclick={() => openFolder("logs")}>
                    <span class="link-icon">
                        <svg
                            width="15"
                            height="15"
                            viewBox="0 0 24 24"
                            fill="none"
                            ><path
                                d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
                                stroke="currentColor"
                                stroke-width="1.8"
                                stroke-linecap="round"
                                stroke-linejoin="round"
                            /></svg
                        >
                    </span>
                    <span class="link-label">Logs Folder</span>
                    <span
                        class="link-sub-path"
                        title={$systemInfo?.logsDir ?? "Logs Directory"}
                        >{$systemInfo?.logsDir ??
                            "logs"}</span
                    >
                    <span class="arrow-indicator">→</span>
                </button>
            {/if}
        </div>
    </div>

    <!-- Software Licenses -->
    <div class="info-card licenses-section">
        <h3>Third-Party Software & Licenses</h3>
        <p class="section-desc">
            LocalTelemetry is built using the following open-source components:
        </p>
        <div class="license-table">
            {#each licenses as lic}
                <div class="license-row">
                    <span class="lic-name">{lic.name}</span>
                    <span class="lic-badge">
                        {lic.type}
                    </span>
                </div>
            {/each}
        </div>
    </div>

    <!-- Footer Copyright -->
    <div class="about-footer">
        <span class="copyright"
            >© {new Date().getFullYear()} Nicconike. GNU GPLv3 License.</span
        >
    </div>
</div>

<style>
    .page-container {
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
        max-width: 540px;
        min-height: 100%;
    }

    /* Brand Hero Header */
    .brand-hero {
        display: flex;
        align-items: center;
        gap: var(--space-5);
        padding: var(--space-6);
        background: linear-gradient(
            135deg,
            var(--color-surface-2) 0%,
            var(--color-surface) 100%
        );
        border: 1px solid var(--color-border);
        border-radius: var(--radius-xl);
        box-shadow: var(--shadow-sm);
    }
    .app-icon {
        width: 48px;
        height: 48px;
        display: block;
    }
    .brand-info {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
    }
    .brand-title {
        font-size: var(--text-lg);
        font-weight: 700;
        letter-spacing: -0.01em;
        color: var(--color-text);
        line-height: 1.2;
    }
    .brand-tagline {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        line-height: 1.4;
    }

    /* Info & Specs Cards */
    .grid-section {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-4);
    }
    @media (max-width: 480px) {
        .grid-section {
            grid-template-columns: 1fr;
        }
    }

    .info-card {
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-lg);
        padding: var(--space-5);
        box-shadow: var(--shadow-sm);
        display: flex;
        flex-direction: column;
    }
    h3 {
        font-size: var(--text-xs);
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--color-text-muted);
        margin-bottom: var(--space-3);
        border-bottom: 1px solid var(--color-divider);
        padding-bottom: var(--space-2);
    }

    /* Specification List */
    .specs-list {
        display: flex;
        flex-direction: column;
        justify-content: space-between;
        flex: 1;
        gap: var(--space-2);
    }
    .spec-item {
        display: flex;
        justify-content: space-between;
        align-items: center;
        font-size: var(--text-sm);
    }
    .spec-label {
        color: var(--color-text-muted);
    }
    .spec-value {
        color: var(--color-text);
        font-weight: 500;
        font-variant-numeric: tabular-nums;
    }
    .spec-value.highlight {
        color: var(--color-primary);
        font-weight: 600;
    }

    /* Software Updates Card */
    .updates-card {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        padding: var(--space-4) var(--space-5);
    }
    .updates-card h3 {
        margin-bottom: var(--space-2);
        padding-bottom: 0;
        border-bottom: none;
    }
    .update-card-content {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
    }
    .update-status-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
        font-size: var(--text-sm);
        padding: 0;
        margin: 0;
    }
    .update-label {
        color: var(--color-text);
        font-weight: 500;
    }
    .update-action-box {
        display: flex;
        align-items: center;
        gap: var(--space-2);
    }

    /* Update Action UI */
    .update-btn {
        background: var(--color-surface-offset);
        color: var(--color-text);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        padding: 4px 12px;
        font-size: var(--text-xs);
        font-weight: 500;
        cursor: pointer;
        transition: all var(--transition);
    }
    .update-btn:hover {
        background: var(--color-primary);
        color: #fff;
        border-color: var(--color-primary);
    }
    .update-badge {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        font-size: var(--text-xs);
        font-weight: 600;
        padding: 3px 10px;
        border-radius: var(--radius-sm);
    }
    .badge-icon {
        display: inline-block;
        flex-shrink: 0;
    }
    .update-badge.checking {
        color: var(--color-text-muted);
        background: var(--color-surface-offset);
    }
    .update-badge.up-to-date {
        color: #4cd964;
        background: rgba(76, 217, 100, 0.12);
    }
    .update-badge.available {
        color: #ff9500;
        background: rgba(255, 149, 0, 0.15);
    }
    .update-badge.error {
        color: #ff3b30;
        background: rgba(255, 59, 48, 0.12);
    }
    .update-found-box {
        display: flex;
        align-items: center;
        gap: var(--space-2);
    }
    .download-btn {
        background: var(--color-primary);
        color: #fff;
        border: none;
        border-radius: var(--radius-sm);
        padding: 4px 12px;
        font-size: var(--text-xs);
        font-weight: 600;
        cursor: pointer;
        transition: opacity var(--transition);
    }
    .download-btn:hover {
        opacity: 0.9;
    }
    .update-settings-sub {
        margin-top: 0;
        padding-top: var(--space-2);
        border-top: 1px solid var(--color-border);
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
    }

    /* Resources Links Stack */
    .links-stack {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
    }
    .link-item {
        display: flex;
        align-items: center;
        width: 100%;
        padding: var(--space-2) var(--space-3);
        background: transparent;
        border: 1px solid transparent;
        border-radius: var(--radius-md);
        color: var(--color-text);
        font-size: var(--text-sm);
        text-align: left;
        cursor: pointer;
        transition: all var(--transition);
    }
    .link-item:hover {
        background: var(--color-surface-offset);
        border-color: var(--color-border);
        color: var(--color-primary);
    }
    .link-icon {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        margin-right: var(--space-3);
        color: var(--color-text-muted);
        transition: color var(--transition);
    }
    .link-item:hover .link-icon {
        color: var(--color-primary);
    }
    .arrow-indicator {
        margin-left: auto;
        color: var(--color-text-faint);
        font-weight: 600;
        transition:
            transform var(--transition),
            color var(--transition);
    }
    .link-item:hover .arrow-indicator {
        color: var(--color-primary);
        transform: translateX(2px);
    }

    .link-sub-path {
        font-size: 11px;
        color: var(--color-text-muted);
        margin-left: var(--space-2);
        max-width: 240px;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }
    @media (max-width: 500px) {
        .link-sub-path {
            display: none;
        }
    }

    /* Third-Party Licenses Section */
    .licenses-section {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
    }
    .section-desc {
        font-size: var(--text-xs);
        color: var(--color-text-muted);
        line-height: 1.4;
        margin-bottom: var(--space-1);
    }
    .license-table {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-2) var(--space-4);
        padding-top: var(--space-1);
    }
    @media (max-width: 480px) {
        .license-table {
            grid-template-columns: 1fr;
            gap: var(--space-2);
        }
    }
    .license-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-2) var(--space-3);
        background: var(--color-surface-offset);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
    }
    .lic-name {
        color: var(--color-text);
        font-weight: 500;
    }
    .lic-badge {
        font-size: 10px;
        font-weight: 700;
        padding: 2px 6px;
        background: var(--color-surface-dynamic);
        border: 1px solid var(--color-border);
        border-radius: 4px;
        color: var(--color-text-muted);
        text-transform: uppercase;
        letter-spacing: 0.02em;
    }

    /* Footer */
    .about-footer {
        display: flex;
        align-items: center;
        justify-content: center;
        padding: var(--space-2) 0 0 0;
        margin-top: 0;
    }
    .copyright {
        font-size: var(--text-xs);
        color: var(--color-text-faint);
    }
</style>

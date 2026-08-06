<script>
    import { systemInfo } from "../../lib/store.ts";
    import { openUrl } from "../../lib/bridge.ts";

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
    // (e.g. 0.1.0.0 -> 0.1.0) but never mangle a SemVer string such as 1.0.0-beta.0.
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
</script>

<div class="page-container">
    <!-- Brand Header -->
    <div class="brand-hero">
        <img src="app.ico" alt="LocalTelemetry Icon" class="app-icon" />
        <div class="brand-info">
            <h1 class="brand-title">LocalTelemetry</h1>
            <p class="brand-tagline">
                Real-time hardware monitoring, embedded right in your Windows
                taskbar.
            </p>
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
                        >{$systemInfo.deploymentMode ?? "Normal"}</span
                    >
                </div>
                <div class="spec-item">
                    <span class="spec-label">Build Date</span>
                    <span class="spec-value"
                        >{$systemInfo.buildDate ?? "-"}</span
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
        padding: var(--space-3) 0;
    }
    .copyright {
        font-size: var(--text-xs);
        color: var(--color-text-faint);
    }
</style>

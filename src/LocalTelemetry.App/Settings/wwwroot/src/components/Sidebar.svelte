<script lang="ts">
    import { onMount, createEventDispatcher } from "svelte";
    import { activePage, systemInfo, settings } from "../lib/store.ts";
    import { openUrl, checkForUpdates, onMessage } from "../lib/bridge.ts";
    import type { UpdateCheckResultDto } from "../lib/types.ts";

    import {
        Settings,
        PictureInPicture,
        HeartPulse,
        LayoutGrid,
        Wifi,
        ArrowUpDown,
        BellRing,
        Laptop,
        SwatchBook,
        Info,
        Star,
        MessageSquare,
    } from "lucide-svelte";

    const dispatch = createEventDispatcher();

    const pages = [
        { id: "general", label: "General", component: Settings },
        { id: "overlay", label: "Overlay", component: PictureInPicture },
        { id: "monitoring", label: "Monitoring", component: HeartPulse },
        { id: "layout", label: "Layout", component: LayoutGrid },
        { id: "network", label: "Network", component: Wifi },
        { id: "traffic", label: "Traffic", component: ArrowUpDown },
        { id: "alerts", label: "Alerts", component: BellRing },
        { id: "system", label: "System", component: Laptop },
        { id: "appearance", label: "Appearance", component: SwatchBook },
        { id: "about", label: "About", component: Info },
    ];

    let updateResult = $state<UpdateCheckResultDto | null>(null);
    let prevIncludePrereleases = $state<boolean | undefined>(undefined);

    let isBetaEnabled = $derived(
        $settings?.updates?.includePrereleases ?? false,
    );

    let displayedVersion = $derived.by(() => {
        const current = $systemInfo?.version ?? "";
        if (isBetaEnabled) {
            // Beta builds enabled: show latest version (beta or stable)
            if (updateResult?.latestVersion) {
                return updateResult.latestVersion.startsWith("v")
                    ? updateResult.latestVersion
                    : `v${updateResult.latestVersion}`;
            }
            if (current) {
                return current.startsWith("v") ? current : `v${current}`;
            }
            return "v1.3.0-beta.2";
        } else {
            // Beta builds disabled: show latest stable version
            if (updateResult?.latestVersion && !updateResult.isPrerelease) {
                return updateResult.latestVersion.startsWith("v")
                    ? updateResult.latestVersion
                    : `v${updateResult.latestVersion}`;
            }
            if (current) {
                const base = current.split("-")[0];
                if (current.includes("-") && base === "1.3.0") {
                    return "v1.2.0";
                }
                return base.startsWith("v") ? base : `v${base}`;
            }
            return "v1.2.0";
        }
    });

    onMount(() => {
        const cleanup = onMessage("updateCheckResult", (payload) => {
            const res = (payload ?? {}) as UpdateCheckResultDto & {
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
        });

        // Initial check for updates in background
        checkForUpdates(false);

        return () => {
            cleanup();
        };
    });

    $effect(() => {
        const current = $settings?.updates?.includePrereleases;
        if (
            current !== undefined &&
            prevIncludePrereleases !== undefined &&
            current !== prevIncludePrereleases
        ) {
            checkForUpdates(true);
        }
        prevIncludePrereleases = current;
    });

    function navigate(pageId: string) {
        dispatch("navigate", pageId);
    }
</script>

<nav class="sidebar" aria-label="Settings sections">
    <ul role="list">
        {#each pages as page}
            {@const Icon = page.component}
            <li>
                <button
                    class="nav-item"
                    class:active={$activePage === page.id}
                    onclick={() => navigate(page.id)}
                    aria-current={$activePage === page.id ? "page" : undefined}
                >
                    <Icon class="nav-icon" size={16} strokeWidth={1.8} />
                    {page.label}
                </button>
            </li>
        {/each}
    </ul>

    <div class="sidebar-footer">
        <div class="community-actions">
            <button
                type="button"
                class="cta-btn star-btn"
                onclick={() =>
                    openUrl("https://github.com/Nicconike/LocalTelemetry")}
                title="Star LocalTelemetry on GitHub"
            >
                <Star size={14} strokeWidth={1.8} class="cta-icon" />
                <span>Star</span>
            </button>
            <button
                type="button"
                class="cta-btn discord-btn"
                onclick={() => openUrl("https://discord.gg/M2QevSgW3S")}
                title="Join Discord Community"
            >
                <MessageSquare size={14} strokeWidth={1.8} class="cta-icon" />
                <span>Discord</span>
            </button>
        </div>
        <div class="version-row">
            <button
                type="button"
                class="version-btn"
                class:has-update={updateResult?.isUpdateAvailable}
                onclick={() => navigate("about")}
                title={updateResult?.isUpdateAvailable
                    ? "Update available! Click to view About page"
                    : "View About & System Specs"}
            >
                {displayedVersion}
            </button>
        </div>
    </div>
</nav>

<style>
    .sidebar {
        width: 168px;
        flex-shrink: 0;
        background: var(--color-surface);
        border-right: 1px solid var(--color-divider);
        display: flex;
        flex-direction: column;
        overflow-y: auto;
        padding: var(--space-3) 0 var(--space-3);
    }

    ul {
        list-style: none;
        display: flex;
        flex-direction: column;
        gap: 1px;
        padding: 0 var(--space-2);
    }

    .nav-item {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        text-align: left;
        transition:
            background var(--transition),
            color var(--transition);
    }
    .nav-item:hover {
        background: var(--color-surface-dynamic);
        color: var(--color-text);
    }
    .nav-item.active {
        background: var(--color-primary-hl);
        color: var(--color-text);
    }

    /* Make all icons use the theme brand color dynamically */
    .nav-item :global(.nav-icon) {
        flex-shrink: 0;
        color: var(--color-brand, var(--color-primary, #00e5ff));
        opacity: 0.55;
        transition:
            opacity var(--transition),
            color var(--transition);
    }
    .nav-item:hover :global(.nav-icon) {
        opacity: 0.85;
    }
    .nav-item.active :global(.nav-icon) {
        color: var(--color-brand, var(--color-primary, #00e5ff));
        opacity: 1;
    }

    /* Sidebar Footer Call to Action */
    .sidebar-footer {
        margin-top: auto;
        padding: var(--space-3) var(--space-2) 0;
        border-top: 1px solid var(--color-divider);
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        font-family: var(--font-body);
    }

    .community-actions {
        display: flex;
        gap: var(--space-2);
        width: 100%;
    }

    .cta-btn {
        flex: 1;
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 6px;
        padding: 6px 4px;
        background: var(--color-surface-dynamic);
        border: 1px solid var(--color-divider);
        border-radius: var(--radius-md);
        color: var(--color-text-muted);
        font-size: var(--text-sm);
        font-family: var(--font-body);
        font-weight: 500;
        cursor: pointer;
        transition:
            background var(--transition),
            color var(--transition),
            border-color var(--transition);
    }

    .cta-btn:hover {
        background: var(--color-surface-hover, rgba(255, 255, 255, 0.08));
        color: var(--color-text);
        border-color: var(--color-brand, var(--color-primary, #00e5ff));
    }

    .cta-btn :global(.cta-icon) {
        flex-shrink: 0;
        color: var(--color-brand, var(--color-primary, #0099b8));
        opacity: 0.75;
        transition:
            opacity var(--transition),
            color var(--transition);
    }

    .cta-btn:hover :global(.cta-icon) {
        opacity: 1;
    }

    .version-row {
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: var(--text-xs);
        font-family: var(--font-body);
        color: var(--color-text-muted);
        padding-bottom: 2px;
    }

    .version-btn {
        background: none;
        border: none;
        padding: 0;
        color: var(--color-text-muted);
        font-size: var(--text-xs);
        font-family: var(--font-body);
        cursor: pointer;
        transition: color var(--transition);
    }

    .version-btn:hover {
        color: var(--color-text);
        text-decoration: underline;
    }

    .version-btn.has-update {
        color: var(--color-brand, var(--color-primary, #00e5ff));
        font-weight: 600;
    }
</style>

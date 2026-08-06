<script>
    import { onMount } from "svelte";
    import {
        settings,
        pristineSettings,
        saveStatus,
        activePage,
        nics,
        allTrafficHistory,
        trafficHistory,
        trafficToday,
        trafficMonths,
        systemInfo,
        importResult,
    } from "../lib/store.ts";
    import {
        requestSettings,
        saveSettings,
        onMessage,
        requestNics,
        requestSystemInfo,
        requestTrafficMonths,
        requestTrafficHistoryAll,
    } from "../lib/bridge.ts";
    import { deepClone } from "../lib/utils.ts";
    import { applyThemeVars } from "../lib/themes.ts";
    import Sidebar from "./Sidebar.svelte";
    import GeneralPage from "./pages/GeneralPage.svelte";
    import MonitoringPage from "./pages/MonitoringPage.svelte";
    import OverlayPage from "./pages/OverlayPage.svelte";
    import LayoutPage from "./pages/LayoutPage.svelte";
    import NetworkPage from "./pages/NetworkPage.svelte";
    import TrafficHistoryPage from "./pages/TrafficHistoryPage.svelte";
    import AlertsPage from "./pages/AlertsPage.svelte";
    import AppearancePage from "./pages/AppearancePage.svelte";
    import SystemPage from "./pages/SystemPage.svelte";
    import AboutPage from "./pages/AboutPage.svelte";

    let saving = $state(false);
    let showNavModal = $state(false);
    let pendingPage = null;
    let navigateAfterSave = null;

    let isDirty = $derived.by(() => {
        if (!$settings || !$pristineSettings) return false;
        return JSON.stringify($settings) !== $pristineSettings;
    });

    $effect(() => {
        $activePage;
        scrollToTop();
    });
    function scrollToTop() {
        const el = document.getElementById("main-content");
        if (el) el.scrollTop = 0;
    }

    onMount(() => {
        const isWebView = !!globalThis.window?.chrome?.webview;
        if (isWebView) {
            const unsub1 = onMessage("settings", (payload) => {
                const clone = deepClone(payload);
                settings.set(clone);
                pristineSettings.set(JSON.stringify(clone));
                applyThemeVars(payload?.windowTheme);
            });

            const unsub2 = onMessage("saved", () => {
                if ($settings) {
                    pristineSettings.set(JSON.stringify($settings));
                }
                saveStatus.set("saved");
                saving = false;
                if (navigateAfterSave) {
                    activePage.set(navigateAfterSave);
                    navigateAfterSave = null;
                }
                setTimeout(() => saveStatus.set("idle"), 2500);
            });

            const unsub3 = onMessage("nics", (payload) => {
                nics.set(payload ?? []);
            });

            const unsub4 = onMessage("systemInfo", (payload) => {
                systemInfo.set(payload ?? {});
            });

            const unsub5 = onMessage("trafficHistoryAll", (payload) => {
                const records = payload.records ?? [];
                allTrafficHistory.set(records);
                const now = new Date();
                const currentMonth = records.filter((r) => {
                    const p = r.date.split("/");
                    return (
                        p.length === 3 &&
                        parseInt(p[1]) === now.getMonth() + 1 &&
                        parseInt(p[2]) === now.getFullYear()
                    );
                });
                trafficHistory.set(currentMonth);
                trafficToday.set({
                    downBytes: payload.todayDown ?? 0,
                    upBytes: payload.todayUp ?? 0,
                });
            });

            const unsub6 = onMessage("trafficMonths", (payload) => {
                trafficMonths.set(payload ?? []);
            });

            const unsub7 = onMessage("trafficToday", (payload) => {
                trafficToday.set({
                    downBytes: payload.downBytes ?? 0,
                    upBytes: payload.upBytes ?? 0,
                });
            });

            const unsub8 = onMessage("importDatResult", (payload) => {
                importResult.set({
                    daysImported: payload?.daysImported ?? 0,
                    error: payload?.error,
                });
                requestTrafficHistoryAll();
                requestTrafficMonths();
            });

            requestSettings();
            requestNics();
            requestSystemInfo();
            requestTrafficMonths();
            requestTrafficHistoryAll();
            return () => {
                unsub1();
                unsub2();
                unsub3();
                unsub4();
                unsub5();
                unsub6();
                unsub7();
                unsub8();
            };
        }
    });

    function handleSave() {
        if (!$settings || saving) return;
        saving = true;
        saveStatus.set("saving");
        saveSettings($settings);
    }

    function handleDiscard() {
        if (!$pristineSettings) return;
        try {
            const restored = JSON.parse($pristineSettings);
            settings.set(restored);
        } catch {}
    }

    function handleNavigateRequest(event) {
        const target = event.detail;
        if (!target || target === $activePage) return;
        if (isDirty) {
            pendingPage = target;
            showNavModal = true;
        } else {
            activePage.set(target);
        }
    }

    function onSaveAndMove() {
        if (!pendingPage || saving) return;
        navigateAfterSave = pendingPage;
        pendingPage = null;
        showNavModal = false;
        handleSave();
    }

    function onDiscardAndMove() {
        handleDiscard();
        if (pendingPage) {
            activePage.set(pendingPage);
            pendingPage = null;
        }
        showNavModal = false;
    }

    function onCancelNav() {
        pendingPage = null;
        showNavModal = false;
    }
</script>

<div class="shell">
    <Sidebar on:navigate={handleNavigateRequest} />
    <div class="content-wrap">
        <main class="content" id="main-content">
            {#if $activePage === "general"}
                <GeneralPage />
            {:else if $activePage === "monitoring"}
                <MonitoringPage />
            {:else if $activePage === "overlay"}
                <OverlayPage />
            {:else if $activePage === "layout"}
                <LayoutPage />
            {:else if $activePage === "network"}
                <NetworkPage />
            {:else if $activePage === "traffic"}
                <TrafficHistoryPage />
            {:else if $activePage === "alerts"}
                <AlertsPage />
            {:else if $activePage === "system"}
                <SystemPage />
            {:else if $activePage === "appearance"}
                <AppearancePage />
            {:else if $activePage === "about"}
                <AboutPage />
            {/if}
        </main>

        <footer
            class="save-bar"
            class:visible={isDirty || $saveStatus !== "idle"}
            class:auto-save={$saveStatus === "saved" && !isDirty}
        >
            {#if isDirty}
                <div class="unsaved-label">
                    <span class="dot"></span> You have unsaved changes
                </div>
                <div class="actions">
                    <button
                        class="btn-discard"
                        onclick={handleDiscard}
                        disabled={saving}
                    >
                        Discard
                    </button>
                    <button
                        class="btn-save"
                        onclick={handleSave}
                        disabled={saving}
                    >
                        {saving ? "Saving…" : "Save Changes"}
                    </button>
                </div>
            {:else if $saveStatus === "saved"}
                <span class="status-saved">✓ Saved</span>
            {:else if $saveStatus === "saving"}
                <span class="status-saving">Saving changes…</span>
            {/if}
        </footer>
    </div>

    {#if showNavModal}
        <div
            class="nav-modal-overlay"
            role="button"
            aria-label="Cancel navigation"
            tabindex="-1"
            onclick={onCancelNav}
            onkeydown={(e) => {
                if (e.key === "Escape") onCancelNav();
            }}
        >
            <div
                class="nav-modal"
                role="dialog"
                aria-modal="true"
                aria-labelledby="nav-modal-title"
                tabindex="-1"
                onclick={(e) => e.stopPropagation()}
                onkeydown={(e) => {
                    if (e.key === "Escape") onCancelNav();
                    e.stopPropagation();
                }}
            >
                <h3 id="nav-modal-title">Unsaved Changes</h3>
                <p>
                    You have unsaved changes on this page. What would you like
                    to do?
                </p>
                <div class="nav-modal-actions">
                    <button class="btn-cancel" onclick={onCancelNav}>
                        Cancel
                    </button>
                    <button class="btn-discard" onclick={onDiscardAndMove}>
                        Discard
                    </button>
                    <button
                        class="btn-save"
                        onclick={onSaveAndMove}
                        disabled={saving}
                    >
                        {saving ? "Saving…" : "Save"}
                    </button>
                </div>
            </div>
        </div>
    {/if}
</div>

<style>
    .shell {
        display: flex;
        height: 100dvh;
        overflow: hidden;
        background: var(--color-bg);
    }
    .content-wrap {
        flex: 1;
        display: flex;
        flex-direction: column;
        overflow: hidden;
    }
    .content {
        flex: 1;
        overflow-y: auto;
        padding: var(--space-5) var(--space-6);
    }

    .save-bar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-3) var(--space-6);
        background: var(--color-surface);
        border-top: 1px solid var(--color-divider);
        transform: translateY(100%);
        transition: transform 200ms cubic-bezier(0.16, 1, 0.3, 1);
        flex-shrink: 0;
        height: auto;
    }
    .save-bar.visible {
        transform: translateY(0);
    }
    .save-bar:not(.visible) {
        height: 0;
        padding: 0;
        border: none;
        overflow: hidden;
    }
    .save-bar.auto-save {
        height: auto;
        padding: var(--space-1) var(--space-6);
        border: none;
        justify-content: center;
    }
    .save-bar.auto-save .status-saved {
        font-size: var(--text-xs);
        color: var(--color-success);
    }
    .status-saving {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
    }
    .status-saved {
        font-size: var(--text-sm);
        color: var(--color-success);
    }
    .unsaved-label {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font-size: var(--text-sm);
        font-weight: 500;
        color: var(--color-text);
    }
    .dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background-color: #f59e0b;
    }
    .actions {
        display: flex;
        align-items: center;
        gap: var(--space-3);
    }
    .btn-discard {
        padding: var(--space-2) var(--space-4);
        border: 1px solid var(--color-divider);
        border-radius: var(--radius-md);
        background: transparent;
        color: var(--color-text-muted);
        font-size: var(--text-sm);
        font-weight: 500;
        cursor: pointer;
        transition:
            background 150ms,
            color 150ms;
    }
    .btn-discard:hover {
        background: var(--color-bg);
        color: var(--color-text);
    }
    .btn-save {
        padding: var(--space-2) var(--space-4);
        border: none;
        border-radius: var(--radius-md);
        background: var(--color-accent, #3b82f6);
        color: #ffffff;
        font-size: var(--text-sm);
        font-weight: 500;
        cursor: pointer;
        transition: opacity 150ms;
    }
    .btn-save:hover {
        opacity: 0.9;
    }

    .nav-modal-overlay {
        position: fixed;
        inset: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        background: oklch(0 0 0 / 0.5);
        z-index: 100;
    }
    .nav-modal {
        width: min(380px, calc(100% - 48px));
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-lg);
        padding: var(--space-5);
        box-shadow: var(--shadow-md);
    }
    .nav-modal h3 {
        font-size: var(--text-base);
        font-weight: 600;
        color: var(--color-text);
        margin-bottom: var(--space-2);
    }
    .nav-modal p {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        line-height: 1.5;
        margin-bottom: var(--space-5);
    }
    .nav-modal-actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-2);
    }
    .nav-modal .btn-save,
    .nav-modal .btn-discard,
    .nav-modal .btn-cancel {
        padding: var(--space-2) var(--space-4);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
        font-weight: 500;
    }
    .nav-modal .btn-save {
        background: var(--color-accent, #3b82f6);
        color: #ffffff;
        border: none;
    }
    .nav-modal .btn-save:disabled {
        opacity: 0.6;
    }
    .nav-modal .btn-discard {
        background: transparent;
        color: var(--color-text);
        border: 1px solid var(--color-divider);
    }
    .nav-modal .btn-discard:hover {
        background: var(--color-surface-offset);
    }
    .nav-modal .btn-cancel {
        background: transparent;
        color: var(--color-text-muted);
        border: 1px solid transparent;
    }
    .nav-modal .btn-cancel:hover {
        color: var(--color-text);
    }
</style>

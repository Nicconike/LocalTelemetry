<script>
    import { settings } from "../../lib/store.ts";
    import { openUrl } from "../../lib/bridge.ts";

    import Toggle from "../ui/Toggle.svelte";
    import Slider from "../ui/Slider.svelte";
    import Select from "../ui/Select.svelte";
    import SectionCard from "../ui/SectionCard.svelte";

    const isLeftAligned = $derived(
        $settings?.overlay?.taskbarAlignment === "left",
    );

    const posOptions = $derived(
        isLeftAligned
            ? [
                  { value: "right", label: "Before System Tray" },
                  { value: "left", label: "Next to Apps" },
              ]
            : [
                  { value: "left", label: "Left Side" },
                  { value: "right", label: "Right Side" },
              ],
    );

    const dblClickOptions = [
        { value: "none", label: "None" },
        { value: "taskmanager", label: "Open Task Manager" },
        { value: "settings", label: "Open Settings" },
    ];

    const displayModeOptions = [
        { value: "primary", label: "Primary Display Only" },
        { value: "secondary", label: "Secondary Display Only" },
        { value: "all", label: "All Displays" },
        { value: "custom", label: "Custom Selection" },
    ];

    function toggleDisplay(id, checked) {
        settings.update((s) => {
            if (!s) return s;
            const current = new Set(s.overlay.enabledDisplays || ["primary"]);
            if (checked) {
                current.add(id);
            } else {
                if (current.size > 1 || !current.has(id)) {
                    current.delete(id);
                }
            }
            return {
                ...s,
                overlay: {
                    ...s.overlay,
                    enabledDisplays: Array.from(current),
                },
            };
        });
    }

    let dismissed = $state(false);

    $effect(() => {
        if (!$settings?.overlay?.taskbarSpaceConstrained) {
            dismissed = false;
        }
    });
</script>

{#if $settings}
    <div class="page">
        <h2>Overlay</h2>
        {#if $settings.overlay?.taskbarSpaceConstrained && !dismissed}
            <div class="constraint-banner" role="alert">
                <svg
                    class="banner-icon"
                    viewBox="0 0 16 16"
                    width="18"
                    height="18"
                    fill="currentColor"
                >
                    <path
                        d="M8.982 1.566a1.13 1.13 0 0 0-1.96 0L.165 13.233c-.457.778.091 1.767.98 1.767h13.713c.889 0 1.438-.99.98-1.767L8.982 1.566zM8 5c.535 0 .954.462.9.995l-.35 3.507a.552.552 0 0 1-1.1 0L7.1 5.995A.905.905 0 0 1 8 5zm.002 6a1 1 0 1 1 0 2 1 1 0 0 1 0-2z"
                    />
                </svg>
                <div class="banner-text">
                    <strong>Taskbar space is constrained.</strong>
                    <span
                        >The taskbar slot is narrower than the overlay.
                        LocalTelemetry has scaled down and clamped the overlay
                        to prevent overlap. Consider reducing metrics or scale.</span
                    >
                    <span class="banner-tip"
                        >Tip: You can hide Windows Widgets to free taskbar
                        space.</span
                    >
                    <div class="banner-actions">
                        <button
                            type="button"
                            class="banner-btn"
                            onclick={() => openUrl("ms-settings:taskbar")}
                            >Open Taskbar Settings</button
                        >
                        <button
                            type="button"
                            class="banner-btn banner-btn-dismiss"
                            onclick={() => (dismissed = true)}>Dismiss</button
                        >
                    </div>
                </div>
            </div>{/if}
        <SectionCard title="Visibility & Interaction">
            <Toggle
                bind:checked={$settings.overlay.visible}
                label="Show Overlay"
                disabled={!$settings.showSystemTrayIcon}
                description={!$settings.showSystemTrayIcon
                    ? "Cannot disable: System Tray Icon is hidden (at least one entry point must remain enabled)"
                    : ""}
            />
            <Select
                label="Double-click action"
                bind:value={$settings.overlay.doubleClickAction}
                options={dblClickOptions}
            />
        </SectionCard>

        <SectionCard title="Position">
            <Select
                label="Position in taskbar"
                bind:value={$settings.overlay.position}
                options={posOptions}
            />
            <Slider
                label="Offset from Edge"
                bind:value={$settings.overlay.offsetX}
                min={0}
                max={200}
                step={4}
                unit="px"
            />
        </SectionCard>

        <SectionCard title="Display">
            <Slider
                label="Opacity"
                bind:value={$settings.overlay.opacity}
                min={20}
                max={100}
                step={5}
                unit="%"
            />
            <Slider
                label="Scale"
                bind:value={$settings.overlay.scale}
                min={80}
                max={200}
                step={5}
                unit="%"
            />
        </SectionCard>
        <SectionCard title="Target Displays">
            <Select
                label="Display mode"
                bind:value={$settings.overlay.displayMode}
                options={displayModeOptions}
            />
            {#if $settings.overlay.displayMode === "secondary" && !($settings.overlay.detectedDisplays || []).some((d) => !d.isPrimary)}
                <div class="display-note">
                    <span>
                        No secondary display detected. The overlay will show on the primary display until another monitor is connected.
                    </span>
                </div>
            {/if}
            {#if $settings.overlay.displayMode === "custom"}
                <div class="display-list">
                    <span class="display-list-title">Enable overlay on:</span>
                    {#each $settings.overlay.detectedDisplays || [] as disp}
                        <label class="display-checkbox-item">
                            <input
                                type="checkbox"
                                checked={$settings.overlay.enabledDisplays?.includes(
                                    disp.id,
                                )}
                                onchange={(e) =>
                                    toggleDisplay(
                                        disp.id,
                                        e.currentTarget.checked,
                                    )}
                            />
                            <span class="display-name">{disp.name}</span>
                            {#if !disp.hasTaskbar}
                                <span class="no-taskbar-badge"
                                    >(No taskbar detected)</span
                                >
                            {/if}
                        </label>
                    {/each}
                </div>
            {/if}
            <div class="display-note">
                <span
                    >Tip: Secondary displays require <strong
                        >Show my taskbar on all displays</strong
                    > enabled in Windows Settings.</span
                >
                <button
                    type="button"
                    class="link-btn"
                    onclick={() => openUrl("ms-settings:taskbar")}
                    >Open Windows Taskbar Settings</button
                >
            </div>
        </SectionCard>
    </div>
{/if}

<style>
    .page {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
    }
    .display-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        padding: var(--space-2) 0;
    }
    .display-list-title {
        color: var(--color-text-muted);
        font-size: var(--text-xs);
        font-weight: 500;
        text-transform: uppercase;
    }
    .display-checkbox-item {
        align-items: center;
        cursor: pointer;
        display: flex;
        font-size: var(--text-sm);
        gap: var(--space-2);
        user-select: none;
    }
    .display-checkbox-item input[type="checkbox"] {
        accent-color: var(--color-primary);
        cursor: pointer;
        height: 16px;
        width: 16px;
    }
    .display-name {
        color: var(--color-text);
    }
    .no-taskbar-badge {
        color: var(--color-warning);
        font-size: var(--text-xs);
        font-style: italic;
    }
    .display-note {
        align-items: flex-start;
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
        margin-top: var(--space-2);
        padding-top: var(--space-2);
        border-top: 1px solid var(--color-border);
        color: var(--color-text-muted);
        font-size: var(--text-xs);
        line-height: 1.4;
    }
    .link-btn {
        background: transparent;
        border: none;
        color: var(--color-primary);
        cursor: pointer;
        font-size: var(--text-xs);
        font-weight: 500;
        padding: 0;
        text-decoration: underline;
        transition: opacity var(--transition);
    }
    .link-btn:hover {
        opacity: 0.8;
    }
    h2 {
        font-size: var(--text-lg);
        font-weight: 600;
    }
    .constraint-banner {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        background: color-mix(
            in srgb,
            var(--color-warning) 12%,
            var(--color-surface)
        );
        border: 1px solid
            color-mix(in srgb, var(--color-warning) 40%, transparent);
        border-radius: var(--radius-md);
        color: var(--color-text);
        font-size: var(--text-sm);
        line-height: 1.4;
    }
    .banner-icon {
        color: var(--color-warning);
        flex-shrink: 0;
        margin-top: 2px;
    }
    .banner-text {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
    }
    .banner-text strong {
        color: var(--color-warning);
    }
    .banner-tip {
        color: var(--color-text-muted);
        font-size: var(--text-xs);
    }
    .banner-actions {
        display: flex;
        gap: var(--space-2);
        margin-top: var(--space-1);
    }
    .banner-btn {
        background: color-mix(in srgb, var(--color-warning) 20%, transparent);
        border: 1px solid
            color-mix(in srgb, var(--color-warning) 50%, transparent);
        border-radius: var(--radius-sm);
        color: var(--color-text);
        cursor: pointer;
        font-size: var(--text-xs);
        font-weight: 500;
        padding: var(--space-1) var(--space-2);
        transition: background var(--transition);
        width: fit-content;
    }
    .banner-btn:hover {
        background: color-mix(in srgb, var(--color-warning) 35%, transparent);
    }
    .banner-btn-dismiss {
        background: transparent;
        border-color: var(--color-border);
        color: var(--color-text-muted);
    }
    .banner-btn-dismiss:hover {
        background: var(--color-surface-hover);
        color: var(--color-text);
    }
</style>

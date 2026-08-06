<script>
    import { settings } from "../../lib/store.ts";
    import Toggle from "../ui/Toggle.svelte";
    import SectionCard from "../ui/SectionCard.svelte";

    const modeLabels = ["Off", "Errors", "Full"];

    const categories = [
        { key: "Cpu", label: "CPU", logKey: "logCpuMode" },
        { key: "Gpu", label: "GPU", logKey: "logGpuMode" },
        { key: "Ram", label: "RAM", logKey: "logRamMode" },
        { key: "Net", label: "Network", logKey: "logNetMode" },
        { key: "Disk", label: "Disk", logKey: "logDiskMode" },
        { key: "Battery", label: "Battery", logKey: "logBatteryMode" },
    ];

    let visibleCategories = $derived(
        $settings.monitoring?.hasBattery
            ? categories
            : categories.filter((c) => c.key !== "Battery"),
    );

    function toggleFileLogging() {
        if ($settings.enableFileLogging) {
            if ($settings.monitoring.logCpuMode === 0)
                $settings.monitoring.logCpuMode = 1;
            if ($settings.monitoring.logGpuMode === 0)
                $settings.monitoring.logGpuMode = 1;
            if ($settings.monitoring.logRamMode === 0)
                $settings.monitoring.logRamMode = 1;
            if ($settings.monitoring.logNetMode === 0)
                $settings.monitoring.logNetMode = 1;
            if ($settings.monitoring.logDiskMode === 0)
                $settings.monitoring.logDiskMode = 1;
            if ($settings.monitoring.logBatteryMode === 0)
                $settings.monitoring.logBatteryMode = 1;
        }
    }

    function onModeChange(category, mode) {
        if (mode > 0 && !$settings.monitoring[`track${category}`]) {
            $settings.monitoring[`track${category}`] = true;
        }
    }
</script>

{#if $settings}
    <div class="page">
        <h2>Monitoring</h2>

        <SectionCard
            title="Active Trackers"
            description="Enable or disable each hardware component. When a component is enabled, all its metrics are polled and displayed in the overlay."
        >
            <div class="tracker-grid">
                {#each visibleCategories as cat}
                    <Toggle
                        bind:checked={$settings.monitoring[`track${cat.key}`]}
                        label={cat.label}
                    />
                {/each}
            </div>
        </SectionCard>

        <SectionCard
            title="GPU Usage Source"
            description="Driver reports kernel busy time (NVML/ADL); WDDM reports max engine utilisation (like Task Manager)."
        >
            <div class="btn-group">
                {#each ["driver", "wddm"] as val}
                    <button
                        class="btn-mode"
                        class:active={$settings.monitoring.gpuUsageSource ===
                            val}
                        onclick={() => {
                            $settings.monitoring.gpuUsageSource = val;
                            change();
                        }}
                    >
                        {val === "wddm"
                            ? "WDDM (Task Manager)"
                            : "Driver (NVML/ADL)"}
                    </button>
                {/each}
            </div>
        </SectionCard>

        <SectionCard
            title="Logging"
            description="Enable debug logging and choose per-category log mode."
        >
            <Toggle
                bind:checked={$settings.enableFileLogging}
                label="Enable Metrics Logging"
                onchange={toggleFileLogging}
            />
            {#if $settings.enableFileLogging}
                <div class="log-grid">
                    {#each visibleCategories as cat}
                        <div class="log-card">
                            <span class="log-label">{cat.label}</span>
                            <div class="btn-group">
                                {#each modeLabels as label, i}
                                    <button
                                        class="btn-mode"
                                        class:active={$settings.monitoring[
                                            cat.logKey
                                        ] === i}
                                        onclick={() => {
                                            $settings.monitoring[cat.logKey] =
                                                i;
                                            onModeChange(cat.key, i);
                                        }}>{label}</button
                                    >
                                {/each}
                            </div>
                        </div>
                    {/each}
                </div>
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
    h2 {
        font-size: var(--text-lg);
        font-weight: 600;
        color: var(--color-text);
    }
    .tracker-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-2);
    }
    .log-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-2);
    }
    .log-card {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-2) var(--space-3);
        border-radius: var(--radius-md);
        gap: var(--space-2);
    }
    .log-label {
        font-size: var(--text-sm);
        font-weight: 500;
        color: var(--color-text);
        white-space: nowrap;
    }
    .btn-group {
        display: flex;
        gap: 0;
        flex-shrink: 0;
    }
    .btn-mode {
        background: var(--color-surface);
        color: var(--color-text-muted);
        border: 1px solid var(--color-border);
        padding: 4px 12px;
        font-size: 12px;
        cursor: pointer;
        transition:
            background 0.15s,
            color 0.15s;
    }
    .btn-mode:first-child {
        border-radius: 4px 0 0 4px;
    }
    .btn-mode:last-child {
        border-radius: 0 4px 4px 0;
    }
    .btn-mode:not(:last-child) {
        border-right: none;
    }
    .btn-mode.active {
        background: var(--color-primary);
        color: #fff;
        border-color: var(--color-primary);
    }

    @media (max-width: 600px) {
        .log-grid {
            grid-template-columns: 1fr;
        }
        .tracker-grid {
            grid-template-columns: 1fr;
        }
    }
</style>

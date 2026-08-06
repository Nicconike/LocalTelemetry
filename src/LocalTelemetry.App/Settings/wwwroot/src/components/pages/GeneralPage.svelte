<script>
    import { settings } from "../../lib/store.ts";
    import Toggle from "../ui/Toggle.svelte";
    import SectionCard from "../ui/SectionCard.svelte";
    import Select from "../ui/Select.svelte";

    const intervalOptions = [
        { value: 500, label: "0.5 s" },
        { value: 1000, label: "1 s" },
        { value: 2000, label: "2 s" },
        { value: 5000, label: "5 s" },
    ];
</script>

{#if $settings}
    <div class="page">
        <h2>General</h2>

        <SectionCard
            title="Startup"
            description="Control how LocalTelemetry launches with Windows."
        >
            <Toggle
                bind:checked={$settings.runAtStartup}
                label="Start with Windows"
            />
            <Toggle
                bind:checked={$settings.startMinimized}
                label="Start minimized"
            />
            <Toggle
                bind:checked={$settings.minimizeToTray}
                label="Minimize to Tray on close"
            />
        </SectionCard>

        <SectionCard
            title="Update Interval"
            description="How often metrics are refreshed."
        >
            <Select
                label="Polling interval"
                bind:value={$settings.monitoring.intervalMs}
                options={intervalOptions}
            />
        </SectionCard>

        <SectionCard title="Units">
            <Toggle
                bind:checked={$settings.monitoring.useFahrenheit}
                label="Show temperatures in °F"
            />
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
</style>

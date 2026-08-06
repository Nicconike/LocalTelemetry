<script>
    import { settings, nics } from "../../lib/store.ts";
    import Toggle from "../ui/Toggle.svelte";
    import Select from "../ui/Select.svelte";
    import SectionCard from "../ui/SectionCard.svelte";

    let nicOptions = $derived([
        { value: "auto", label: "Auto (Best Match)" },
        ...($nics ?? []).map((n) => ({ value: n, label: n })),
    ]);
</script>

{#if $settings}
    <div class="page">
        <h2>Network</h2>

        <SectionCard
            title="Network Adapter"
            description="Select which network adapter is used for throughput monitoring and daily traffic logging."
        >
            <Select
                label="Adapter"
                bind:value={$settings.monitoring.preferredNic}
                options={nicOptions}
            />
        </SectionCard>

        <SectionCard
            title="Internet Traffic Logging"
            description="Automatically tracks daily upload/download totals from the selected adapter. Data is stored persistently."
        >
            <Toggle
                bind:checked={$settings.netUsage.enabled}
                label="Enable daily traffic logging"
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
    }
</style>

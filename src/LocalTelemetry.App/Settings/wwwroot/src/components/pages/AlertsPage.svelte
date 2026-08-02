<script>
    import { settings } from "../../lib/store.ts";
    import Toggle from "../ui/Toggle.svelte";
    import Slider from "../ui/Slider.svelte";
    import SectionCard from "../ui/SectionCard.svelte";

    function change() {
        checkDirty();
    }
</script>

{#if $settings}
    <div class="page">
        <h2>Alerts</h2>

        <SectionCard title="Enable">
            <Toggle
                bind:checked={$settings.alerts.enabled}
                label="Enable Threshold Alerts"
                onchange={change}
            />
        </SectionCard>

        {#if $settings.alerts.enabled}
            <SectionCard title="Thresholds">
                <h3 class="section-subtitle">CPU</h3>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertCpuUsage}
                        onchange={change}
                    />
                    <Slider
                        label="CPU Usage"
                        bind:value={$settings.alerts.cpuUsageMaxPct}
                        min={50}
                        max={100}
                        unit="%"
                        onchange={change}
                    />
                </div>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertCpuTemp}
                        onchange={change}
                    />
                    <Slider
                        label="CPU Temp"
                        bind:value={$settings.alerts.cpuTempMaxC}
                        min={60}
                        max={100}
                        unit="°C"
                        onchange={change}
                    />
                </div>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertCpuFreq}
                        onchange={change}
                    />
                    <Slider
                        label="CPU Frequency (alert when below)"
                        bind:value={$settings.alerts.cpuFreqMinMhz}
                        min={200}
                        max={2000}
                        step={50}
                        unit="MHz"
                        onchange={change}
                    />
                </div>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertCpuPower}
                        onchange={change}
                    />
                    <Slider
                        label="CPU Package Power"
                        bind:value={$settings.alerts.cpuPowerMaxW}
                        min={20}
                        max={250}
                        step={5}
                        unit="W"
                        onchange={change}
                    />
                </div>

                <h3 class="section-subtitle">RAM</h3>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertRamUsage}
                        onchange={change}
                    />
                    <Slider
                        label="RAM Usage"
                        bind:value={$settings.alerts.ramUsageMaxPct}
                        min={50}
                        max={100}
                        unit="%"
                        onchange={change}
                    />
                </div>

                <h3 class="section-subtitle">GPU</h3>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertGpuUsage}
                        onchange={change}
                    />
                    <Slider
                        label="GPU Usage"
                        bind:value={$settings.alerts.gpuUsageMaxPct}
                        min={50}
                        max={100}
                        unit="%"
                        onchange={change}
                    />
                </div>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertGpuVram}
                        onchange={change}
                    />
                    <Slider
                        label="GPU VRAM Usage"
                        bind:value={$settings.alerts.gpuVramMaxMb}
                        min={1024}
                        max={32768}
                        step={512}
                        unit="MB"
                        onchange={change}
                    />
                </div>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertGpuTemp}
                        onchange={change}
                    />
                    <Slider
                        label="GPU Temp"
                        bind:value={$settings.alerts.gpuTempMaxC}
                        min={60}
                        max={100}
                        unit="°C"
                        onchange={change}
                    />
                </div>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertGpuFreq}
                        onchange={change}
                    />
                    <Slider
                        label="GPU Frequency (alert when below)"
                        bind:value={$settings.alerts.gpuFreqMinMhz}
                        min={100}
                        max={2000}
                        step={50}
                        unit="MHz"
                        onchange={change}
                    />
                </div>
                <div class="alert-row">
                    <Toggle
                        bind:checked={$settings.alerts.alertGpuPower}
                        onchange={change}
                    />
                    <Slider
                        label="GPU Power"
                        bind:value={$settings.alerts.gpuPowerMaxW}
                        min={20}
                        max={400}
                        step={5}
                        unit="W"
                        onchange={change}
                    />
                </div>

                {#if $settings.hasBattery}
                    <h3 class="section-subtitle">Battery</h3>
                    <div class="alert-row">
                        <Toggle
                            bind:checked={$settings.alerts.alertBatteryLow}
                            onchange={change}
                        />
                        <Slider
                            label="Battery Low"
                            bind:value={$settings.alerts.batteryLowPct}
                            min={5}
                            max={50}
                            unit="%"
                            onchange={change}
                        />
                    </div>
                {/if}
            </SectionCard>

            <SectionCard
                title="Actions"
                description="What happens when a threshold is crossed."
            >
                <Toggle
                    bind:checked={$settings.alerts.showToastNotif}
                    label="Show tray notification"
                    onchange={change}
                />
                <Toggle
                    bind:checked={$settings.alerts.flashOverlay}
                    label="Flash overlay briefly"
                    onchange={change}
                />
                <Slider
                    label="Cooldown between alerts"
                    bind:value={$settings.alerts.cooldownSecs}
                    min={5}
                    max={120}
                    step={5}
                    unit="s"
                    onchange={change}
                />
                <Toggle
                    bind:checked={$settings.alerts.fireOncePerSession}
                    label="Only fire once per session (ignores cooldown)"
                    onchange={change}
                />
            </SectionCard>
        {/if}
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
    h3.section-subtitle {
        font-size: var(--text-sm);
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: var(--color-text-muted);
        margin: var(--space-3) 0 var(--space-2) 0;
    }
    h3.section-subtitle:first-of-type {
        margin-top: 0;
    }

    .alert-row {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
    }

    .alert-row > :global(.slider-wrap) {
        flex: 1;
        min-width: 0;
    }

    .alert-row > :global(.toggle) {
        margin-top: 2px;
        flex-shrink: 0;
    }
</style>

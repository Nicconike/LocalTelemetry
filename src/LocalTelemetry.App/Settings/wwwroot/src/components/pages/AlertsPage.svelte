<script>
    import { settings } from "../../lib/store.ts";
    import Toggle from "../ui/Toggle.svelte";
    import Slider from "../ui/Slider.svelte";
    import SectionCard from "../ui/SectionCard.svelte";

    function onMasterToggleChange(e) {
        if (!$settings?.alerts) return;
        const isChecked = e?.target
            ? e.target.checked
            : $settings.alerts.enabled;
        if (!isChecked) return;

        // If user is enabling alerts and no metric is selected, auto-enable sensible safety defaults
        const anyActive =
            $settings.alerts.alertCpuUsage ||
            $settings.alerts.alertCpuTemp ||
            $settings.alerts.alertCpuFreq ||
            $settings.alerts.alertCpuPower ||
            $settings.alerts.alertRamUsage ||
            $settings.alerts.alertGpuUsage ||
            $settings.alerts.alertGpuVram ||
            $settings.alerts.alertGpuTemp ||
            $settings.alerts.alertGpuFreq ||
            $settings.alerts.alertGpuPower ||
            $settings.alerts.alertNetDaily ||
            $settings.alerts.alertNetMonthly ||
            $settings.alerts.alertBatteryLow;

        if (!anyActive) {
            $settings.alerts.alertCpuTemp = true;
            $settings.alerts.cpuTempMaxC = 90;
            $settings.alerts.alertRamUsage = true;
            $settings.alerts.ramUsageMaxPct = 90;
            $settings.alerts.alertGpuTemp = true;
            $settings.alerts.gpuTempMaxC = 85;
        }
    }

    let hasAnyAlertActive = $derived(
        Boolean(
            $settings?.alerts &&
                ($settings.alerts.alertCpuUsage ||
                    $settings.alerts.alertCpuTemp ||
                    $settings.alerts.alertCpuFreq ||
                    $settings.alerts.alertCpuPower ||
                    $settings.alerts.alertRamUsage ||
                    $settings.alerts.alertGpuUsage ||
                    $settings.alerts.alertGpuVram ||
                    $settings.alerts.alertGpuTemp ||
                    $settings.alerts.alertGpuFreq ||
                    $settings.alerts.alertGpuPower ||
                    $settings.alerts.alertNetDaily ||
                    $settings.alerts.alertNetMonthly ||
                    $settings.alerts.alertBatteryLow),
        ),
    );
</script>

{#if $settings}
    <div class="page">
        <h2>Alerts</h2>

        <SectionCard title="Enable">
            <Toggle
                bind:checked={$settings.alerts.enabled}
                label="Enable Threshold Alerts"
                onchange={onMasterToggleChange}
            />
        </SectionCard>

        {#if $settings.alerts.enabled}
            {#if !hasAnyAlertActive}
                <div class="alert-empty-notice">
                    <svg
                        class="notice-icon"
                        width="16"
                        height="16"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="2"
                        stroke-linecap="round"
                        stroke-linejoin="round"
                    >
                        <circle cx="12" cy="12" r="10" />
                        <line x1="12" y1="8" x2="12" y2="12" />
                        <line x1="12" y1="16" x2="12.01" y2="16" />
                    </svg>
                    <span>
                        No metrics are currently selected. Enable one or more
                        thresholds below to receive alerts.
                    </span>
                </div>
            {/if}

            <SectionCard title="Thresholds">
                <h3 class="section-subtitle">CPU</h3>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertCpuUsage} />
                    <Slider
                        label="CPU Usage"
                        bind:value={$settings.alerts.cpuUsageMaxPct}
                        min={50}
                        max={100}
                        unit="%"
                    />
                </div>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertCpuTemp} />
                    <Slider
                        label="CPU Temp"
                        bind:value={$settings.alerts.cpuTempMaxC}
                        min={60}
                        max={100}
                        unit="°C"
                    />
                </div>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertCpuFreq} />
                    <Slider
                        label="CPU Frequency (alert when below)"
                        bind:value={$settings.alerts.cpuFreqMinMhz}
                        min={200}
                        max={2000}
                        step={50}
                        unit="MHz"
                    />
                </div>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertCpuPower} />
                    <Slider
                        label="CPU Package Power"
                        bind:value={$settings.alerts.cpuPowerMaxW}
                        min={20}
                        max={250}
                        step={5}
                        unit="W"
                    />
                </div>

                <h3 class="section-subtitle">RAM</h3>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertRamUsage} />
                    <Slider
                        label="RAM Usage"
                        bind:value={$settings.alerts.ramUsageMaxPct}
                        min={50}
                        max={100}
                        unit="%"
                    />
                </div>

                <h3 class="section-subtitle">GPU</h3>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertGpuUsage} />
                    <Slider
                        label="GPU Usage"
                        bind:value={$settings.alerts.gpuUsageMaxPct}
                        min={50}
                        max={100}
                        unit="%"
                    />
                </div>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertGpuVram} />
                    <Slider
                        label="GPU VRAM Usage"
                        bind:value={$settings.alerts.gpuVramMaxMb}
                        min={1024}
                        max={32768}
                        step={512}
                        unit="MB"
                    />
                </div>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertGpuTemp} />
                    <Slider
                        label="GPU Temp"
                        bind:value={$settings.alerts.gpuTempMaxC}
                        min={60}
                        max={100}
                        unit="°C"
                    />
                </div>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertGpuFreq} />
                    <Slider
                        label="GPU Frequency (alert when below)"
                        bind:value={$settings.alerts.gpuFreqMinMhz}
                        min={100}
                        max={2000}
                        step={50}
                        unit="MHz"
                    />
                </div>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertGpuPower} />
                    <Slider
                        label="GPU Power"
                        bind:value={$settings.alerts.gpuPowerMaxW}
                        min={20}
                        max={400}
                        step={5}
                        unit="W"
                    />
                </div>

                <h3 class="section-subtitle">Network</h3>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertNetDaily} />
                    <Slider
                        label="Daily Internet Usage"
                        bind:value={$settings.alerts.netDailyMaxGb}
                        min={5}
                        max={500}
                        step={5}
                        unit="GB"
                    />
                </div>
                <div class="alert-row">
                    <Toggle bind:checked={$settings.alerts.alertNetMonthly} />
                    <Slider
                        label="Monthly Internet Usage"
                        bind:value={$settings.alerts.netMonthlyMaxGb}
                        min={100}
                        max={5000}
                        step={50}
                        unit="GB"
                    />
                </div>

                {#if $settings.monitoring?.hasBattery}
                    <h3 class="section-subtitle">Battery</h3>
                    <div class="alert-row">
                        <Toggle
                            bind:checked={$settings.alerts.alertBatteryLow}
                        />
                        <Slider
                            label="Battery Low"
                            bind:value={$settings.alerts.batteryLowPct}
                            min={5}
                            max={50}
                            unit="%"
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
                    label="Show Tray Notification"
                />
                <Toggle
                    bind:checked={$settings.alerts.flashOverlay}
                    label="Flash Overlay Briefly"
                />
                <Slider
                    label="Cooldown between alerts"
                    bind:value={$settings.alerts.cooldownSecs}
                    min={5}
                    max={120}
                    step={5}
                    unit="s"
                />
                <Toggle
                    bind:checked={$settings.alerts.fireOncePerSession}
                    label="Only fire once per session (ignores cooldown)"
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

    .alert-empty-notice {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-3) var(--space-4);
        border-radius: var(--radius-md);
        background: rgba(245, 158, 11, 0.12);
        border: 1px solid rgba(245, 158, 11, 0.3);
        color: #fbbf24;
        font-size: var(--text-sm);
        line-height: 1.4;
    }

    .notice-icon {
        flex-shrink: 0;
        color: #fbbf24;
    }
</style>

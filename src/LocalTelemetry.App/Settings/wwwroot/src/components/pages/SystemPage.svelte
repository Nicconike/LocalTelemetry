<script>
    import { systemInfo } from "../../lib/store.ts";

    function busTypeToLabel(t) {
        if (!t) return "DISK";
        t = t.toUpperCase();
        if (t.startsWith("BUSTYPE") || t === "DISK" || t === "SCSI")
            return "DISK";
        return t;
    }

    let info = $derived($systemInfo);
</script>

<div class="page">
    <h2>System</h2>

    {#if info?.os !== undefined}
        <div class="hw-card">
            <div class="hw-body">
                <div class="sec-header">System</div>
                <div class="hw-row">
                    <span class="hw-label">Device Name</span>
                    <span class="hw-value">{info.deviceName}</span>
                </div>
                <div class="hw-row">
                    <span class="hw-label">OS</span>
                    <span class="hw-value">{info.os}</span>
                </div>
                <div class="hw-row">
                    <span class="hw-label">System Type</span>
                    <span class="hw-value">{info.systemType || "Desktop"}</span>
                </div>

                <div class="sec-divider"></div>

                <div class="sec-header">Motherboard</div>
                {#if info.systemModel}
                    <div class="hw-row">
                        <span class="hw-label">Model</span>
                        <span class="hw-value">{info.systemModel}</span>
                    </div>
                {/if}
                {#if info.motherboardMfr}
                    <div class="hw-row">
                        <span class="hw-label">Motherboard</span>
                        <span class="hw-value"
                            >{info.motherboardMfr}{" "}{#if info.motherboardModel}
                                {info.motherboardModel}{/if}</span
                        >
                    </div>
                {:else if info.motherboardModel}
                    <div class="hw-row">
                        <span class="hw-label">Motherboard</span>
                        <span class="hw-value">{info.motherboardModel}</span>
                    </div>
                {/if}
                {#if info.motherboardVersion}
                    <div class="hw-row">
                        <span class="hw-label">Version</span>
                        <span class="hw-value">{info.motherboardVersion}</span>
                    </div>
                {/if}
                {#if info.motherboardSerial}
                    <div class="hw-row">
                        <span class="hw-label">Serial</span>
                        <span class="hw-value">{info.motherboardSerial}</span>
                    </div>
                {/if}
                {#if info.bios}
                    <div class="hw-row">
                        <span class="hw-label">BIOS</span>
                        <span class="hw-value">{info.bios}</span>
                    </div>
                {/if}
                {#if info.biosUefi !== undefined}
                    <div class="hw-row">
                        <span class="hw-label">Mode</span>
                        <span class="hw-value"
                            >{info.biosUefi ? "UEFI" : "Legacy"}</span
                        >
                    </div>
                {/if}
                {#if info.systemType !== "Laptop"}
                    {#if info.psu}
                        <div class="hw-row">
                            <span class="hw-label">PSU</span>
                            <span class="hw-value">{info.psu}</span>
                        </div>
                    {/if}
                    {#if info.psuCapacity}
                        <div class="hw-row">
                            <span class="hw-label">Rating</span>
                            <span class="hw-value">{info.psuCapacity}</span>
                        </div>
                    {/if}
                {/if}

                <div class="sec-divider"></div>

                <div class="sec-header">Processor</div>
                <div class="hw-row">
                    <span class="hw-label">Model</span>
                    <span class="hw-value">{info.cpu}</span>
                </div>
                {#if info.cpuVendor}
                    <div class="hw-row">
                        <span class="hw-label">Vendor</span>
                        <span class="hw-value">{info.cpuVendor}</span>
                    </div>
                {/if}
                {#if info.cpuCores}
                    <div class="hw-row">
                        <span class="hw-label">Cores</span>
                        <span class="hw-value">{info.cpuCores}</span>
                    </div>
                {/if}
                {#if info.cpuThreads}
                    <div class="hw-row">
                        <span class="hw-label">Threads</span>
                        <span class="hw-value">{info.cpuThreads}</span>
                    </div>
                {/if}
                {#if info.cpuBaseSpeedMhz}
                    <div class="hw-row">
                        <span class="hw-label">Base Clock</span>
                        <span class="hw-value"
                            >{(info.cpuBaseSpeedMhz / 1000).toFixed(1)} GHz</span
                        >
                    </div>
                {/if}
                {#if info.cpuMaxSpeedMhz}
                    <div class="hw-row">
                        <span class="hw-label">Max Turbo</span>
                        <span class="hw-value"
                            >{(info.cpuMaxSpeedMhz / 1000).toFixed(1)} GHz</span
                        >
                    </div>
                {/if}
                {#if info.cpuSocket}
                    <div class="hw-row">
                        <span class="hw-label">Socket</span>
                        <span class="hw-value">{info.cpuSocket}</span>
                    </div>
                {/if}
                {#if info.cpuTdpWatts}
                    <div class="hw-row">
                        <span class="hw-label">TDP</span>
                        <span class="hw-value">{info.cpuTdpWatts} W</span>
                    </div>
                {/if}

                <div class="sec-divider"></div>

                <div class="sec-header">Memory</div>
                <div class="hw-row">
                    <span class="hw-label">Installed RAM</span>
                    <span class="hw-value">
                        {#if info.installedRamGb && info.installedRamGb > 0}
                            {info.installedRamGb.toFixed(1)} GB
                            {#if info.ramGb}
                                ({info.ramGb} GB usable)
                            {/if}
                        {:else if info.ramGb}
                            {info.ramGb} GB
                        {/if}
                    </span>
                </div>
                {#if info.ramMfr}
                    <div class="hw-row">
                        <span class="hw-label">Manufacturer</span>
                        <span class="hw-value">{info.ramMfr}</span>
                    </div>
                {/if}
                {#if info.ramSpeed}
                    <div class="hw-row">
                        <span class="hw-label">Speed</span>
                        <span class="hw-value">{info.ramSpeed}</span>
                    </div>
                {/if}
                {#if info.ramType}
                    <div class="hw-row">
                        <span class="hw-label">Type</span>
                        <span class="hw-value">{info.ramType}</span>
                    </div>
                {/if}
                {#if info.ramSlots > 0}
                    <div class="hw-row">
                        <span class="hw-label">Modules</span>
                        <span class="hw-value">{info.ramSlots} slot(s)</span>
                    </div>
                {/if}
                {#if info.ramModules?.length}
                    {#each info.ramModules as mod, i}
                        <div class="hw-row">
                            <span class="hw-label">Module {i + 1}</span>
                            <span class="hw-value"
                                >{mod.sizeGb} GB {mod.speed}</span
                            >
                        </div>
                    {/each}
                {/if}

                <div class="sec-divider"></div>

                <div class="sec-header">Graphics</div>
                {#if info.gpus?.length}
                    {#each info.gpus as gpu}
                        <div class="hw-row">
                            <span class="hw-label"
                                >{gpu.dedicated
                                    ? "Dedicated"
                                    : "Integrated"}</span
                            >
                            <span class="hw-value">{gpu.name}</span>
                        </div>
                        {#if gpu.vramGb}
                            <div class="hw-row">
                                <span class="hw-label">VRAM</span>
                                <span class="hw-value">{gpu.vramGb} GB</span>
                            </div>
                        {/if}
                        {#if gpu.driver}
                            <div class="hw-row">
                                <span class="hw-label">Driver</span>
                                <span class="hw-value">{gpu.driver}</span>
                            </div>
                        {/if}
                        {#if gpu.tdpW}
                            <div class="hw-row">
                                <span class="hw-label">TDP</span>
                                <span class="hw-value">{gpu.tdpW}</span>
                            </div>
                        {/if}
                    {/each}
                {:else}
                    <div class="hw-row">
                        <span class="hw-label">Adapter</span>
                        <span class="hw-value">None detected</span>
                    </div>
                {/if}

                <div class="sec-divider"></div>

                <div class="sec-header">Storage</div>
                {#if info.disks?.length}
                    {#each info.disks as disk}
                        <div class="hw-row">
                            <span class="hw-label"
                                >Disk {disk.diskIndex + 1}</span
                            >
                            <span class="hw-value" title={disk.model}>
                                {disk.model}
                                {#if disk.sizeGb}
                                    ({disk.sizeGb}){/if}
                                {#if disk.busType}
                                    [{busTypeToLabel(disk.busType)}]{/if}
                                {#if disk.boot}
                                    <span class="boot-badge">Boot</span>
                                {/if}
                            </span>
                        </div>
                    {/each}
                {:else}
                    <div class="hw-row">
                        <span class="hw-label">Drive</span>
                        <span class="hw-value">None detected</span>
                    </div>
                {/if}

                <div class="sec-divider"></div>

                <div class="sec-header">Network</div>
                {#if info.nics?.length}
                    {#each info.nics as nic}
                        <div class="hw-row">
                            <span class="hw-label">Adapter</span>
                            <span class="hw-value">{nic}</span>
                        </div>
                    {/each}
                {:else}
                    <div class="hw-row">
                        <span class="hw-label">Adapter</span>
                        <span class="hw-value">None detected</span>
                    </div>
                {/if}

                {#if info.systemType === "Laptop"}
                    <div class="sec-divider"></div>

                    <div class="sec-header">Battery</div>
                    {#if info.batteryManufacturer}
                        <div class="hw-row">
                            <span class="hw-label">Manufacturer</span>
                            <span class="hw-value"
                                >{info.batteryManufacturer}</span
                            >
                        </div>
                    {/if}
                    {#if info.batteryDeviceName}
                        <div class="hw-row">
                            <span class="hw-label">Device Name</span>
                            <span class="hw-value"
                                >{info.batteryDeviceName}</span
                            >
                        </div>
                    {/if}
                    {#if info.batteryDesignCapacity}
                        <div class="hw-row">
                            <span class="hw-label">Designed Capacity</span>
                            <span class="hw-value"
                                >{info.batteryDesignCapacity}</span
                            >
                        </div>
                    {/if}
                    {#if info.batteryFullChargedCapacity}
                        <div class="hw-row">
                            <span class="hw-label">Full Charge Capacity</span>
                            <span class="hw-value"
                                >{info.batteryFullChargedCapacity}</span
                            >
                        </div>
                    {/if}
                {/if}
            </div>
        </div>
    {:else}
        <div class="loading">
            <p>Detecting hardware&hellip;</p>
        </div>
    {/if}
</div>

<style>
    .page {
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
    }
    h2 {
        font-size: var(--text-lg);
        font-weight: 600;
        margin: 0;
    }
    .hw-card {
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-lg);
        overflow: hidden;
    }
    .hw-body {
        padding: var(--space-2) var(--space-4);
    }
    .hw-row {
        display: flex;
        align-items: baseline;
        gap: var(--space-3);
        padding: var(--space-1) 0;
    }
    .hw-label {
        font-size: var(--text-xs);
        color: var(--color-text-muted);
        min-width: 130px;
        flex-shrink: 0;
    }
    .hw-value {
        font-size: var(--text-sm);
        color: var(--color-text);
    }
    .sec-divider {
        height: 1px;
        background: var(--color-border);
        margin: var(--space-3) 0;
    }
    .sec-header {
        font-size: var(--text-xs);
        font-weight: 600;
        color: var(--color-text-muted);
        text-transform: uppercase;
        letter-spacing: 0.05em;
        margin: var(--space-3) 0 var(--space-1) 0;
    }
    .sec-header:first-of-type {
        margin-top: 0;
    }
    .loading {
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-lg);
        padding: var(--space-8);
        text-align: center;
    }
    .loading p {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
    }
    .boot-badge {
        font-size: 10px;
        background: var(--color-accent, #4a9eff);
        color: #fff;
        padding: 1px 5px;
        border-radius: 3px;
        margin-left: 4px;
        vertical-align: middle;
    }
</style>

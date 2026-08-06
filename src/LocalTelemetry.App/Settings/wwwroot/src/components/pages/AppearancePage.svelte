<script>
    import { settings, systemInfo } from "../../lib/store.ts";
    import { themes, applyThemeVars } from "../../lib/themes.ts";
    import { saveSettings } from "../../lib/bridge.ts";
    import SectionCard from "../ui/SectionCard.svelte";

    const metricLabels = {
        cpu_pct: "CPU Usage",
        cpu_temp: "CPU Temp",
        cpu_freq: "CPU Frequency",
        cpu_power: "CPU Package Power",
        ram_pct: "RAM Usage",
        ram_used: "RAM Used",
        gpu_pct: "GPU Usage",
        gpu_temp: "GPU Temp",
        gpu_vram: "GPU VRAM Usage",
        gpu_freq: "GPU Frequency",
        gpu_power: "GPU Power",
        net_down: "Download",
        net_up: "Upload",
        net_total: "Total Transferred",
        battery_pct: "Battery",
        battery_rate: "Charge Rate",
    };

    const fallbackHex = "#cdccca";

    let darkThemes = $derived(
        themes.filter((t) => !t.value.endsWith("-light")),
    );
    let lightThemes = $derived(
        themes.filter((t) => t.value.endsWith("-light")),
    );

    let diskMetrics = $derived(
        !$systemInfo?.disks
            ? []
            : $systemInfo.disks.flatMap((d, i) => {
                  const displayIdx = i + 1;
                  return [
                      {
                          id: `disk_disk${i}_read`,
                          label: d.busType
                              ? `Disk ${displayIdx} Read (${d.busType})`
                              : `Disk ${displayIdx} Read`,
                      },
                      {
                          id: `disk_disk${i}_write`,
                          label: d.busType
                              ? `Disk ${displayIdx} Write (${d.busType})`
                              : `Disk ${displayIdx} Write`,
                      },
                  ];
              }),
    );

    const leftColumnGroups = [
        {
            key: "cpu",
            group: "CPU",
            ids: ["cpu_pct", "cpu_temp", "cpu_freq", "cpu_power"],
        },
        {
            key: "gpu",
            group: "GPU",
            ids: ["gpu_pct", "gpu_temp", "gpu_vram", "gpu_freq", "gpu_power"],
        },
    ];

    let rightColumnGroups = $derived([
        { key: "ram", group: "RAM", ids: ["ram_pct", "ram_used"] },
        {
            key: "network",
            group: "Network",
            ids: ["net_down", "net_up", "net_total"],
        },
        ...($settings?.monitoring?.hasBattery
            ? [
                  {
                      key: "battery",
                      group: "Battery",
                      ids: ["battery_pct", "battery_rate"],
                  },
              ]
            : []),
    ]);

    let invalidHex = $state({});

    function applyTheme(t) {
        if (!t || !$settings) return;
        applyThemeVars(t.value);
        $settings.windowTheme = t.value;
        settings.set($settings);
        saveSettings($settings);
    }

    function restoreDefaults() {
        applyThemeVars("default");
        $settings.windowTheme = "default";
        settings.set($settings);
        saveSettings($settings);
    }

    function getMetricColor(id) {
        return $settings?.overlay?.metricColors?.[id] ?? fallbackHex;
    }

    function getDefaultColor(id) {
        return $settings?.overlay?.defaultMetricColors?.[id] ?? fallbackHex;
    }

    function hasOverride(id) {
        return (
            $settings?.overlay?.userCustomizedMetricColors?.includes(id) ??
            false
        );
    }

    function getGroupColor(key, ids) {
        return (
            $settings?.overlay?.groupColors?.[key] ??
            (ids?.length ? getMetricColor(ids[0]) : fallbackHex)
        );
    }

    const groupDefaultKey = {
        cpu: "cpu_pct",
        gpu: "gpu_pct",
        ram: "ram_pct",
        network: "net_down",
        battery: "battery_pct",
    };

    function getDefaultGroupColor(key, ids) {
        const id = key === "disk" ? ids?.[0] : groupDefaultKey[key];
        return id ? getDefaultColor(id) : fallbackHex;
    }

    function hasGroupOverride(key) {
        return (
            $settings?.overlay?.userCustomizedGroupColors?.includes(key) ??
            false
        );
    }

    function setCustomized(id, value) {
        const def = getDefaultColor(id);
        if (value === def) {
            if ($settings.overlay.userCustomizedMetricColors)
                $settings.overlay.userCustomizedMetricColors =
                    $settings.overlay.userCustomizedMetricColors.filter(
                        (x) => x !== id,
                    );
        } else {
            if (!$settings.overlay.userCustomizedMetricColors)
                $settings.overlay.userCustomizedMetricColors = [];
            if (!$settings.overlay.userCustomizedMetricColors.includes(id))
                $settings.overlay.userCustomizedMetricColors.push(id);
        }
    }

    function setGroupCustomized(grp, value) {
        const def = getDefaultGroupColor(grp.key, grp.ids);
        if (value === def) {
            if ($settings.overlay.userCustomizedGroupColors)
                $settings.overlay.userCustomizedGroupColors =
                    $settings.overlay.userCustomizedGroupColors.filter(
                        (x) => x !== grp.key,
                    );
        } else {
            if (!$settings.overlay.userCustomizedGroupColors)
                $settings.overlay.userCustomizedGroupColors = [];
            if (!$settings.overlay.userCustomizedGroupColors.includes(grp.key))
                $settings.overlay.userCustomizedGroupColors.push(grp.key);
        }
    }

    function onColorChange(e, id) {
        const color = e.target.value;
        const current = getMetricColor(id);
        if (color === current) return;
        if (!$settings.overlay.metricColors)
            $settings.overlay.metricColors = {};
        $settings.overlay.metricColors[id] = color;
        setCustomized(id, color);
        settings.set($settings);
        saveSettings($settings);
    }

    function onHexBlur(id, value) {
        if (/^#[0-9a-f]{6}$/i.test(value)) {
            invalidHex = { ...invalidHex, [id]: false };
            const current = getMetricColor(id);
            if (value === current) return;
            if (!$settings.overlay.metricColors)
                $settings.overlay.metricColors = {};
            $settings.overlay.metricColors[id] = value;
            setCustomized(id, value);
            settings.set($settings);
            saveSettings($settings);
        } else if (value.length > 0) {
            invalidHex = { ...invalidHex, [id]: true };
        }
    }

    function applyGroupColor(grp, color) {
        if (!$settings.overlay.groupColors) $settings.overlay.groupColors = {};
        if (getGroupColor(grp.key) !== color)
            $settings.overlay.groupColors[grp.key] = color;
        setGroupCustomized(grp, color);
        if (!$settings.overlay.metricColors)
            $settings.overlay.metricColors = {};
        for (const id of grp.ids) {
            const current = getMetricColor(id);
            if (color === current) continue;
            $settings.overlay.metricColors[id] = color;
            setCustomized(id, color);
        }
        settings.set($settings);
        saveSettings($settings);
    }

    function onGroupColorChange(e, grp) {
        applyGroupColor(grp, e.target.value);
    }

    function onGroupHexBlur(key, ids, value) {
        const invalidKey = "group-" + key;
        if (/^#[0-9a-f]{6}$/i.test(value)) {
            invalidHex = { ...invalidHex, [invalidKey]: false };
            if (value === getGroupColor(key)) return;
            applyGroupColor({ key, ids }, value);
        } else if (value.length > 0) {
            invalidHex = { ...invalidHex, [invalidKey]: true };
        }
    }

    function resetColor(id) {
        const def = getDefaultColor(id);
        if (!$settings.overlay.metricColors)
            $settings.overlay.metricColors = {};
        $settings.overlay.metricColors[id] = def;
        if ($settings.overlay.userCustomizedMetricColors)
            $settings.overlay.userCustomizedMetricColors =
                $settings.overlay.userCustomizedMetricColors.filter(
                    (x) => x !== id,
                );
        settings.set($settings);
        saveSettings($settings);
    }

    function resetGroupColors(key, ids) {
        for (const id of ids) {
            const def = getDefaultColor(id);
            if (!$settings.overlay.metricColors)
                $settings.overlay.metricColors = {};
            $settings.overlay.metricColors[id] = def;
        }
        if ($settings.overlay.userCustomizedMetricColors)
            $settings.overlay.userCustomizedMetricColors =
                $settings.overlay.userCustomizedMetricColors.filter(
                    (x) => !ids.includes(x),
                );
        if (!$settings.overlay.groupColors) $settings.overlay.groupColors = {};
        $settings.overlay.groupColors[key] = getDefaultGroupColor(key, ids);
        if ($settings.overlay.userCustomizedGroupColors)
            $settings.overlay.userCustomizedGroupColors =
                $settings.overlay.userCustomizedGroupColors.filter(
                    (x) => x !== key,
                );
        settings.set($settings);
        saveSettings($settings);
    }

    function resetAllColors() {
        if (!$settings?.overlay?.defaultMetricColors) return;
        $settings.overlay.metricColors = {
            ...$settings.overlay.defaultMetricColors,
        };
        $settings.overlay.userCustomizedMetricColors = [];
        const nextGroups = {};
        for (const grp of [...leftColumnGroups, ...rightColumnGroups]) {
            nextGroups[grp.key] = getDefaultGroupColor(grp.key, grp.ids);
        }
        if (diskMetrics.length > 0) {
            nextGroups.disk = getDefaultGroupColor(
                "disk",
                diskMetrics.map((d) => d.id),
            );
        }
        $settings.overlay.groupColors = nextGroups;
        $settings.overlay.userCustomizedGroupColors = [];
        settings.set($settings);
        saveSettings($settings);
    }

    function onThemeToggle(e) {
        $settings.overlay.followWindowsTheme = e.target.checked;
        settings.set($settings);
        saveSettings($settings);
    }
</script>

{#if $settings}
    <div class="page">
        <h2>Appearance</h2>

        <SectionCard
            title="App Theme"
            description="Choose a color scheme for this settings window"
        >
            <h3 class="section-subtitle">Dark Themes</h3>
            <div class="theme-preview">
                {#each darkThemes as t}
                    <div
                        class="swatch-card"
                        class:active={$settings.windowTheme === t.value}
                        onclick={() => applyTheme(t)}
                        role="button"
                        tabindex="0"
                        onkeydown={(e) => e.key === "Enter" && applyTheme(t)}
                    >
                        <span class="swatch-label">{t.label}</span>
                        <div
                            class="swatch-preview"
                            style="background:{t.vars['--color-bg']}"
                        >
                            <div
                                class="swatch-surface"
                                style="background:{t.vars[
                                    '--color-surface'
                                ]};border-color:{t.vars['--color-divider']}"
                            >
                                <div class="swatch-content">
                                    <span
                                        class="swatch-bar"
                                        style="background:{t.vars[
                                            '--color-primary'
                                        ]}"
                                    ></span>
                                    <span
                                        class="swatch-text-line"
                                        style="background:{t.vars[
                                            '--color-text-muted'
                                        ]}"
                                    ></span>
                                </div>
                                <div class="swatch-dots">
                                    <span
                                        class="swatch-dot"
                                        style="background:{t.vars[
                                            '--color-success'
                                        ]}"
                                    ></span>
                                    <span
                                        class="swatch-dot"
                                        style="background:{t.vars[
                                            '--color-warning'
                                        ]}"
                                    ></span>
                                </div>
                            </div>
                        </div>
                    </div>
                {/each}
            </div>

            <h3 class="section-subtitle">Light Themes</h3>
            <div class="theme-preview">
                {#each lightThemes as t}
                    <div
                        class="swatch-card"
                        class:active={$settings.windowTheme === t.value}
                        onclick={() => applyTheme(t)}
                        role="button"
                        tabindex="0"
                        onkeydown={(e) => e.key === "Enter" && applyTheme(t)}
                    >
                        <span class="swatch-label">{t.label}</span>
                        <div
                            class="swatch-preview"
                            style="background:{t.vars['--color-bg']}"
                        >
                            <div
                                class="swatch-surface"
                                style="background:{t.vars[
                                    '--color-surface'
                                ]};border-color:{t.vars['--color-divider']}"
                            >
                                <div class="swatch-content">
                                    <span
                                        class="swatch-bar"
                                        style="background:{t.vars[
                                            '--color-primary'
                                        ]}"
                                    ></span>
                                    <span
                                        class="swatch-text-line"
                                        style="background:{t.vars[
                                            '--color-text-muted'
                                        ]}"
                                    ></span>
                                </div>
                                <div class="swatch-dots">
                                    <span
                                        class="swatch-dot"
                                        style="background:{t.vars[
                                            '--color-success'
                                        ]}"
                                    ></span>
                                    <span
                                        class="swatch-dot"
                                        style="background:{t.vars[
                                            '--color-warning'
                                        ]}"
                                    ></span>
                                </div>
                            </div>
                        </div>
                    </div>
                {/each}
            </div>
            {#if $settings.windowTheme !== "default"}
                <button class="reset-btn" onclick={restoreDefaults}>
                    Reset to Default Theme
                </button>
            {/if}
        </SectionCard>

        <SectionCard
            title="Taskbar Widget Metric Colors"
            description="Colors for each metric's value shown in the taskbar widget. Detected from your hardware by default"
        >
            <div class="color-section">
                <div class="theme-toggle">
                    <label class="toggle-label">
                        <input
                            type="checkbox"
                            checked={$settings.overlay.followWindowsTheme}
                            onchange={onThemeToggle}
                        />
                        Follow Windows taskbar theme (auto background/text)
                    </label>
                </div>

                <div class="color-columns">
                    <div class="color-column">
                        {#each leftColumnGroups as grp}
                            <h3 class="section-subtitle">
                                {grp.group}
                                {#if grp.ids.some( (id) => hasOverride(id), ) || hasGroupOverride(grp.key)}
                                    <button
                                        class="reset-btn group"
                                        onclick={() =>
                                            resetGroupColors(grp.key, grp.ids)}
                                        >↺ Reset</button
                                    >
                                {/if}
                            </h3>
                            <div class="group-color-row">
                                <span class="color-label">All {grp.group}</span>
                                <input
                                    type="color"
                                    value={getGroupColor(grp.key, grp.ids)}
                                    onchange={(e) => onGroupColorChange(e, grp)}
                                    class="color-picker"
                                />
                                <input
                                    type="text"
                                    class="hex-input"
                                    class:invalid={invalidHex[
                                        "group-" + grp.key
                                    ]}
                                    value={getGroupColor(grp.key, grp.ids)}
                                    onblur={(e) =>
                                        onGroupHexBlur(
                                            grp.key,
                                            grp.ids,
                                            e.target.value,
                                        )}
                                    placeholder="#RRGGBB"
                                />
                                <span class="reset-spacer"></span>
                            </div>
                            {#each grp.ids as id}
                                {@const label = metricLabels[id] ?? id}
                                {@const color = getMetricColor(id)}
                                {@const def = getDefaultColor(id)}
                                <div class="color-row">
                                    <span class="color-label">{label}</span>
                                    <input
                                        type="color"
                                        value={color}
                                        onchange={(e) => onColorChange(e, id)}
                                        class="color-picker"
                                    />
                                    <input
                                        type="text"
                                        class="hex-input"
                                        class:invalid={invalidHex[id]}
                                        value={color}
                                        onblur={(e) =>
                                            onHexBlur(id, e.target.value)}
                                        placeholder="#RRGGBB"
                                    />
                                    {#if hasOverride(id)}
                                        <button
                                            class="reset-btn small"
                                            onclick={() => resetColor(id)}
                                            title="Reset to default">↺</button
                                        >
                                    {:else if color !== def}
                                        <button
                                            class="reset-btn small"
                                            onclick={() => resetColor(id)}
                                            title="Reset to default">↺</button
                                        >
                                    {:else}
                                        <span class="reset-spacer"></span>
                                    {/if}
                                </div>
                            {/each}
                        {/each}
                    </div>

                    <div class="color-column">
                        {#each rightColumnGroups as grp}
                            <h3 class="section-subtitle">
                                {grp.group}
                                {#if grp.ids.some( (id) => hasOverride(id), ) || hasGroupOverride(grp.key)}
                                    <button
                                        class="reset-btn group"
                                        onclick={() =>
                                            resetGroupColors(grp.key, grp.ids)}
                                        >↺ Reset</button
                                    >
                                {/if}
                            </h3>
                            <div class="group-color-row">
                                <span class="color-label">All {grp.group}</span>
                                <input
                                    type="color"
                                    value={getGroupColor(grp.key, grp.ids)}
                                    onchange={(e) => onGroupColorChange(e, grp)}
                                    class="color-picker"
                                />
                                <input
                                    type="text"
                                    class="hex-input"
                                    class:invalid={invalidHex[
                                        "group-" + grp.key
                                    ]}
                                    value={getGroupColor(grp.key, grp.ids)}
                                    onblur={(e) =>
                                        onGroupHexBlur(
                                            grp.key,
                                            grp.ids,
                                            e.target.value,
                                        )}
                                    placeholder="#RRGGBB"
                                />
                                <span class="reset-spacer"></span>
                            </div>
                            {#each grp.ids as id}
                                {@const label = metricLabels[id] ?? id}
                                {@const color = getMetricColor(id)}
                                {@const def = getDefaultColor(id)}
                                <div class="color-row">
                                    <span class="color-label">{label}</span>
                                    <input
                                        type="color"
                                        value={color}
                                        onchange={(e) => onColorChange(e, id)}
                                        class="color-picker"
                                    />
                                    <input
                                        type="text"
                                        class="hex-input"
                                        class:invalid={invalidHex[id]}
                                        value={color}
                                        onblur={(e) =>
                                            onHexBlur(id, e.target.value)}
                                        placeholder="#RRGGBB"
                                    />
                                    {#if hasOverride(id)}
                                        <button
                                            class="reset-btn small"
                                            onclick={() => resetColor(id)}
                                            title="Reset to default">↺</button
                                        >
                                    {:else if color !== def}
                                        <button
                                            class="reset-btn small"
                                            onclick={() => resetColor(id)}
                                            title="Reset to default">↺</button
                                        >
                                    {:else}
                                        <span class="reset-spacer"></span>
                                    {/if}
                                </div>
                            {/each}
                        {/each}

                        {#if diskMetrics.length > 0}
                            <h3 class="section-subtitle">
                                Disk
                                {#if diskMetrics.some( ({ id }) => hasOverride(id), ) || hasGroupOverride("disk")}
                                    <button
                                        class="reset-btn group"
                                        onclick={() =>
                                            resetGroupColors(
                                                "disk",
                                                diskMetrics.map((d) => d.id),
                                            )}>↺ Reset</button
                                    >
                                {/if}
                            </h3>
                            <div class="group-color-row">
                                <span class="color-label">All Disks</span>
                                <input
                                    type="color"
                                    value={getGroupColor(
                                        "disk",
                                        diskMetrics.map((d) => d.id),
                                    )}
                                    onchange={(e) =>
                                        onGroupColorChange(e, {
                                            key: "disk",
                                            ids: diskMetrics.map((d) => d.id),
                                        })}
                                    class="color-picker"
                                />
                                <input
                                    type="text"
                                    class="hex-input"
                                    class:invalid={invalidHex["group-disk"]}
                                    value={getGroupColor(
                                        "disk",
                                        diskMetrics.map((d) => d.id),
                                    )}
                                    onblur={(e) =>
                                        onGroupHexBlur(
                                            "disk",
                                            diskMetrics.map((d) => d.id),
                                            e.target.value,
                                        )}
                                    placeholder="#RRGGBB"
                                />
                                <span class="reset-spacer"></span>
                            </div>
                            {#each diskMetrics as { id, label }}
                                {@const color = getMetricColor(id)}
                                {@const def = getDefaultColor(id)}
                                <div class="color-row">
                                    <span class="color-label">{label}</span>
                                    <input
                                        type="color"
                                        value={color}
                                        onchange={(e) => onColorChange(e, id)}
                                        class="color-picker"
                                    />
                                    <input
                                        type="text"
                                        class="hex-input"
                                        class:invalid={invalidHex[id]}
                                        value={color}
                                        onblur={(e) =>
                                            onHexBlur(id, e.target.value)}
                                        placeholder="#RRGGBB"
                                    />
                                    {#if hasOverride(id)}
                                        <button
                                            class="reset-btn small"
                                            onclick={() => resetColor(id)}
                                            title="Reset to default">↺</button
                                        >
                                    {:else if color !== def}
                                        <button
                                            class="reset-btn small"
                                            onclick={() => resetColor(id)}
                                            title="Reset to default">↺</button
                                        >
                                    {:else}
                                        <span class="reset-spacer"></span>
                                    {/if}
                                </div>
                            {/each}
                        {/if}
                    </div>
                </div>

                {#if $settings?.overlay?.userCustomizedMetricColors?.length > 0 || $settings?.overlay?.userCustomizedGroupColors?.length > 0}
                    <button class="reset-btn" onclick={resetAllColors}>
                        Reset All to Defaults
                    </button>
                {/if}
            </div>
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

    .theme-preview {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-3);
    }
    .theme-preview + h3.section-subtitle {
        margin-top: var(--space-4);
        padding-top: var(--space-3);
        border-top: 1px solid var(--color-divider);
    }

    .swatch-card {
        width: 120px;
        border-radius: var(--radius-lg);
        border: 2px solid var(--color-border);
        cursor: pointer;
        transition: border-color var(--transition);
        overflow: hidden;
        background: transparent;
    }
    .swatch-card:hover {
        border-color: var(--color-text-muted);
    }
    .swatch-card.active {
        border-color: var(--color-primary);
    }

    .swatch-label {
        display: block;
        padding: var(--space-2) var(--space-3) 0;
        font-size: var(--text-xs);
        font-weight: 600;
        text-align: center;
    }
    .swatch-preview {
        padding: 6px;
    }
    .swatch-surface {
        border: 1px solid;
        border-radius: 3px;
        padding: 4px;
        display: flex;
        flex-direction: column;
        gap: 3px;
    }
    .swatch-content {
        display: flex;
        align-items: center;
        gap: 4px;
    }
    .swatch-bar {
        width: 3px;
        height: 12px;
        border-radius: 1px;
        flex-shrink: 0;
    }
    .swatch-text-line {
        height: 6px;
        border-radius: 1px;
        flex: 1;
        opacity: 0.5;
    }
    .swatch-dots {
        display: flex;
        gap: 3px;
    }
    .swatch-dot {
        width: 5px;
        height: 5px;
        border-radius: 50%;
    }
    .reset-btn {
        margin-top: var(--space-3);
        padding: var(--space-2) var(--space-4);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        border: 1px solid var(--color-border);
        background: transparent;
        cursor: pointer;
        transition: all var(--transition);
    }
    .reset-btn:hover {
        color: var(--color-text);
        border-color: var(--color-text-muted);
    }
    .reset-btn.small {
        margin-top: 0;
        padding: 0 6px;
        font-size: 16px;
        line-height: 28px;
        min-width: 28px;
        text-align: center;
    }
    .reset-btn.group {
        margin-top: 0;
        padding: 0 8px;
        font-size: var(--text-xs);
        line-height: 22px;
        margin-left: var(--space-2);
        vertical-align: middle;
    }

    .color-section {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
    }

    .color-columns {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-6);
    }

    .color-column {
        flex: 1;
        min-width: min(100%, 240px);
        display: grid;
        grid-template-columns: auto 36px 60px 28px;
        gap: var(--space-1) var(--space-3);
        align-items: center;
    }
    .color-column .section-subtitle {
        grid-column: 1 / -1;
    }
    .color-column .group-color-row {
        grid-column: 1 / -1;
        display: grid;
        grid-template-columns: subgrid;
        gap: var(--space-1) var(--space-3);
        align-items: center;
        padding: var(--space-1) var(--space-2);
        margin-bottom: var(--space-1);
        border-radius: var(--radius-md);
        background: var(--color-bg-subtle, rgba(128, 128, 128, 0.08));
    }

    .theme-toggle {
        margin-bottom: var(--space-1);
    }
    .toggle-label {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        cursor: pointer;
    }
    .toggle-label input[type="checkbox"] {
        width: 16px;
        height: 16px;
        cursor: pointer;
    }

    .color-row {
        display: contents;
    }
    .color-label {
        font-size: var(--text-sm);
        font-weight: 500;
        color: var(--color-text);
    }
    .color-picker {
        width: 36px;
        height: 30px;
        padding: 0;
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        cursor: pointer;
        background: none;
    }
    .color-picker::-webkit-color-swatch-wrapper {
        padding: 2px;
    }
    .color-picker::-webkit-color-swatch {
        border: none;
        border-radius: var(--radius-sm);
    }
    .hex-input {
        font-size: var(--text-xs);
        font-family: monospace;
        color: var(--color-text);
        background: var(--color-bg);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        padding: 2px 4px;
        width: 60px;
        min-width: 0;
        outline: none;
        transition: border-color var(--transition);
    }
    .hex-input:focus {
        border-color: var(--color-primary);
    }
    .hex-input.invalid {
        border-color: var(--color-danger, #e74c3c);
        box-shadow: 0 0 0 1px var(--color-danger, #e74c3c);
    }
    .reset-spacer {
        width: 28px;
    }
</style>

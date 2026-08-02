<script>
    import { settings } from "../../lib/store.ts";
    import { saveSettings } from "../../lib/bridge.ts";
    import Toggle from "../ui/Toggle.svelte";
    import Slider from "../ui/Slider.svelte";
    import Select from "../ui/Select.svelte";
    import SectionCard from "../ui/SectionCard.svelte";

    const posOptions = [
        { value: "left", label: "Left side" },
        { value: "right", label: "Right side" },
    ];

    const dblClickOptions = [
        { value: "none", label: "None" },
        { value: "taskmanager", label: "Open Task Manager" },
        { value: "settings", label: "Open Settings" },
    ];

    function change() {
        checkDirty();
    }

    function apply() {
        saveSettings($settings);
    }
</script>

{#if $settings}
    <div class="page">
        <h2>Overlay</h2>

        <SectionCard title="Visibility & Interaction">
            <Toggle
                bind:checked={$settings.overlay.visible}
                label="Show Overlay"
                onchange={apply}
            />
            <Select
                label="Double-click action"
                bind:value={$settings.overlay.doubleClickAction}
                options={dblClickOptions}
                onchange={change}
            />
        </SectionCard>

        <SectionCard title="Position">
            <Select
                label="Position in taskbar"
                bind:value={$settings.overlay.position}
                options={posOptions}
                onchange={change}
            />
            <Slider
                label="Offset from Edge"
                bind:value={$settings.overlay.offsetX}
                min={0}
                max={200}
                step={4}
                unit="px"
                onchange={apply}
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
                onchange={apply}
            />
            <Slider
                label="Scale"
                bind:value={$settings.overlay.scale}
                min={80}
                max={200}
                step={5}
                unit="%"
                onchange={apply}
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

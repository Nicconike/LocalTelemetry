<script>
    let {
        value = $bindable(50),
        min = 0,
        max = 100,
        step = 1,
        label = "",
        unit = "",
        onchange,
    } = $props();
</script>

<div class="slider-wrap">
    {#if label}
        <div class="slider-header">
            <span class="lbl">{label}</span>
            <span class="val">{value}{unit}</span>
        </div>
    {/if}
    <input
        type="range"
        bind:value
        {min}
        {max}
        {step}
        aria-label={label}
        aria-valuemin={min}
        aria-valuemax={max}
        aria-valuenow={value}
        {onchange}
    />
</div>

<style>
    .slider-wrap {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
    }
    .slider-header {
        display: flex;
        justify-content: space-between;
    }
    .lbl {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
    }
    .val {
        font-size: var(--text-sm);
        color: var(--color-primary);
        font-variant-numeric: tabular-nums;
    }

    input[type="range"] {
        -webkit-appearance: none;
        appearance: none;
        width: 100%;
        height: 4px;
        border-radius: var(--radius-full);
        background: var(--color-surface-offset);
        outline: none;
        cursor: pointer;
    }
    input[type="range"]::-webkit-slider-thumb {
        -webkit-appearance: none;
        width: 14px;
        height: 14px;
        border-radius: var(--radius-full);
        background: var(--color-primary);
        border: 2px solid var(--color-bg);
        transition:
            background var(--transition),
            transform var(--transition);
    }
    input[type="range"]::-webkit-slider-thumb:hover {
        background: var(--color-primary-hover);
        transform: scale(1.2);
    }
    input[type="range"]:focus-visible::-webkit-slider-thumb {
        outline: 2px solid var(--color-primary);
        outline-offset: 2px;
    }
</style>

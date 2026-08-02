<script>
    let {
        checked = $bindable(false),
        disabled = false,
        label = "",
        onchange,
    } = $props();
</script>

<label class="toggle" class:disabled>
    <input
        type="checkbox"
        bind:checked
        {disabled}
        aria-label={label}
        {onchange}
    />
    <span class="track">
        <span class="thumb"></span>
    </span>
    {#if label}
        <span class="lbl">{label}</span>
    {/if}
</label>

<style>
    .toggle {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        cursor: pointer;
        user-select: none;
    }
    .toggle.disabled {
        opacity: 0.45;
        pointer-events: none;
    }

    input {
        position: absolute;
        opacity: 0;
        width: 0;
        height: 0;
    }

    .track {
        position: relative;
        width: 34px;
        height: 18px;
        border-radius: var(--radius-full);
        background: var(--color-surface-offset);
        border: 1px solid var(--color-border);
        transition:
            background var(--transition),
            border-color var(--transition);
        flex-shrink: 0;
    }
    .thumb {
        position: absolute;
        top: 2px;
        left: 2px;
        width: 12px;
        height: 12px;
        border-radius: var(--radius-full);
        background: var(--color-text-muted);
        transition:
            transform var(--transition),
            background var(--transition);
    }
    input:checked ~ .track {
        background: var(--color-primary);
        border-color: var(--color-primary-hover);
    }
    input:checked ~ .track .thumb {
        transform: translateX(16px);
        background: #fff;
    }

    .lbl {
        font-size: var(--text-sm);
        color: var(--color-text);
    }
</style>

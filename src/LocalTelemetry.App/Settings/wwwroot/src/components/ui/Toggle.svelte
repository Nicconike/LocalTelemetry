<script>
    let {
        checked = $bindable(false),
        disabled = false,
        label = "",
        description = "",
        onchange,
    } = $props();
</script>

<label class="toggle" class:disabled class:has-desc={!!description}>
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
    {#if label || description}
        <span class="lbl-col">
            {#if label}<span class="lbl">{label}</span>{/if}
            {#if description}<span class="desc">{description}</span>{/if}
        </span>
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
    .toggle.has-desc {
        align-items: flex-start;
    }
    .toggle.has-desc .track {
        margin-top: 1px;
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

    .lbl-col {
        display: flex;
        flex-direction: column;
        gap: 1px;
    }

    .lbl {
        font-size: var(--text-sm);
        color: var(--color-text);
        line-height: 1.3;
    }

    .desc {
        font-size: var(--text-xs);
        color: var(--color-text-muted);
        line-height: 1.3;
    }
</style>

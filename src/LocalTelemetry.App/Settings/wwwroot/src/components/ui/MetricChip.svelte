<script>
    let { label = "", active = false, onclick } = $props();
</script>

<button
    class="chip"
    class:active
    role="checkbox"
    aria-checked={active}
    {onclick}
>
    {label}
</button>

<style>
    .chip {
        display: inline-flex;
        align-items: center;
        padding: var(--space-1) var(--space-3);
        border-radius: var(--radius-full);
        font-size: var(--text-xs);
        font-weight: 500;
        border: 1px solid var(--color-border);
        background: var(--color-surface-2);
        color: var(--color-text-muted);
        cursor: pointer;
        transition: all var(--transition);
    }
    .chip:hover {
        border-color: var(--color-primary);
        color: var(--color-text);
    }
    .chip.active {
        background: var(--color-primary-hl);
        border-color: var(--color-primary);
        color: var(--color-primary);
    }
</style>

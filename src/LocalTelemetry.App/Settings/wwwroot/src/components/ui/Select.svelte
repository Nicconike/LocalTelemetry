<script>
    let {
        value = $bindable(""),
        options = [],
        label = "",
        id = "sel-" + Math.random().toString(36).slice(2),
        onchange,
    } = $props();
</script>

<div class="sel-wrap">
    {#if label}
        <label for={id} class="lbl">{label}</label>
    {/if}
    <div class="sel-outer">
        <select {id} bind:value {onchange}>
            {#each options as opt}
                <option value={opt.value}>{opt.label}</option>
            {/each}
        </select>
        <svg class="arrow" viewBox="0 0 16 16" fill="none" aria-hidden="true">
            <path
                d="M4 6l4 4 4-4"
                stroke="currentColor"
                stroke-width="1.5"
                stroke-linecap="round"
                stroke-linejoin="round"
            />
        </svg>
    </div>
</div>

<style>
    .sel-wrap {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
    }
    .lbl {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
    }

    .sel-outer {
        position: relative;
        display: flex;
        align-items: center;
    }
    select {
        appearance: none;
        -webkit-appearance: none;
        width: 100%;
        padding: var(--space-2) var(--space-6) var(--space-2) var(--space-3);
        background: var(--color-surface-2);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
        color: var(--color-text);
        cursor: pointer;
        transition: border-color var(--transition);
    }
    select:hover {
        border-color: var(--color-primary);
    }
    select:focus-visible {
        border-color: var(--color-primary);
    }
    select option {
        background: var(--color-surface-2);
    }

    .arrow {
        position: absolute;
        right: var(--space-2);
        width: 16px;
        height: 16px;
        color: var(--color-text-muted);
        pointer-events: none;
    }
</style>

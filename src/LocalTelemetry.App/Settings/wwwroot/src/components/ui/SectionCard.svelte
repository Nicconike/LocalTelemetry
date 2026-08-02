<script>
    let { title = "", description = "", children } = $props();
</script>

<div class="card">
    {#if title}
        <div class="card-head">
            <h3>{title}</h3>
            {#if description}<p class="desc">{description}</p>{/if}
        </div>
    {/if}
    <div class="card-body">
        {@render children?.()}
    </div>
</div>

<style>
    .card {
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-lg);
        overflow: hidden;
    }
    .card-head {
        padding: var(--space-4) var(--space-5);
        border-bottom: 1px solid var(--color-divider);
    }
    h3 {
        font-size: var(--text-base);
        font-weight: 600;
        color: var(--color-text);
    }
    .desc {
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        margin-top: var(--space-1);
    }
    .card-body {
        padding: var(--space-4) var(--space-5);
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
    }
</style>

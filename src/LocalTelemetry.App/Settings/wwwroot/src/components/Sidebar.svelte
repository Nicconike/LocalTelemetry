<script>
    import { createEventDispatcher } from "svelte";
    import { activePage } from "../lib/store.ts";

    import {
        Settings,
        PictureInPicture,
        HeartPulse,
        LayoutGrid,
        Wifi,
        ArrowUpDown,
        BellRing,
        Laptop,
        SwatchBook,
        Info,
    } from "lucide-svelte";

    const dispatch = createEventDispatcher();

    const pages = [
        { id: "general", label: "General", component: Settings },
        { id: "overlay", label: "Overlay", component: PictureInPicture },
        { id: "monitoring", label: "Monitoring", component: HeartPulse },
        { id: "layout", label: "Layout", component: LayoutGrid },
        { id: "network", label: "Network", component: Wifi },
        { id: "traffic", label: "Traffic", component: ArrowUpDown },
        { id: "alerts", label: "Alerts", component: BellRing },
        { id: "system", label: "System", component: Laptop },
        { id: "appearance", label: "Appearance", component: SwatchBook },
        { id: "about", label: "About", component: Info },
    ];

    function navigate(pageId) {
        dispatch("navigate", pageId);
    }
</script>

<nav class="sidebar" aria-label="Settings sections">
    <ul role="list">
        {#each pages as page}
            <li>
                <button
                    class="nav-item"
                    class:active={$activePage === page.id}
                    onclick={() => navigate(page.id)}
                    aria-current={$activePage === page.id ? "page" : undefined}
                >
                    <svelte:component
                        this={page.component}
                        class="nav-icon"
                        size={16}
                        strokeWidth={1.8}
                    />
                    {page.label}
                </button>
            </li>
        {/each}
    </ul>
</nav>

<style>
    .sidebar {
        width: 168px;
        flex-shrink: 0;
        background: var(--color-surface);
        border-right: 1px solid var(--color-divider);
        display: flex;
        flex-direction: column;
        overflow-y: auto;
        padding: var(--space-3) 0 var(--space-4);
    }

    ul {
        list-style: none;
        display: flex;
        flex-direction: column;
        gap: 1px;
        padding: 0 var(--space-2);
    }

    .nav-item {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
        color: var(--color-text-muted);
        text-align: left;
        transition:
            background var(--transition),
            color var(--transition);
    }
    .nav-item:hover {
        background: var(--color-surface-dynamic);
        color: var(--color-text);
    }
    .nav-item.active {
        background: var(--color-primary-hl);
        color: var(--color-text);
    }

    /* Make all icons use the theme brand color dynamically */
    .nav-item :global(.nav-icon) {
        flex-shrink: 0;
        color: var(--color-brand, var(--color-primary, #00e5ff));
        opacity: 0.55;
        transition: opacity var(--transition), color var(--transition);
    }
    .nav-item:hover :global(.nav-icon) {
        opacity: 0.85;
    }
    .nav-item.active :global(.nav-icon) {
        color: var(--color-brand, var(--color-primary, #00e5ff));
        opacity: 1;
    }
</style>

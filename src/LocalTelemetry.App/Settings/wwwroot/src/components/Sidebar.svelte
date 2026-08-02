<script>
    import { activePage } from "../lib/store.ts";

    const pages = [
        {
            id: "general",
            label: "General",
            icon: "M12 1a2 2 0 0 0-2 2v.3a6.9 6.9 0 0 0-2.4 1l-.2-.2a2 2 0 0 0-2.8.8l-.7 1.1a2 2 0 0 0 .8 2.7l.2.2a7 7 0 0 0 0 2l-.2.2a2 2 0 0 0-.8 2.7l.7 1.2a2 2 0 0 0 2.8.8l.2-.2a6.9 6.9 0 0 0 2.4 1v.3a2 2 0 0 0 4 0v-.3a6.9 6.9 0 0 0 2.4-1l.2.2a2 2 0 0 0 2.8-.8l.7-1.2a2 2 0 0 0-.8-2.7l-.2-.2a7 7 0 0 0 0-2l.2-.2a2 2 0 0 0 .8-2.7l-.7-1.1a2 2 0 0 0-2.8-.8l-.2.2a6.9 6.9 0 0 0-2.4-1V3a2 2 0 0 0-2-2zm0 7a4 4 0 1 1 0 8 4 4 0 0 1 0-8z",
        },
        {
            id: "monitoring",
            label: "Monitoring",
            icon: "M18 20V10M12 20V4M6 20v-6",
        },
        {
            id: "overlay",
            label: "Overlay",
            icon: "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5",
        },
        {
            id: "layout",
            label: "Layout",
            icon: "M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z",
        },
        { id: "network", label: "Network", icon: "M22 12h-4l-3 9-4-18-3 9H2" },
        { id: "traffic", label: "Traffic", icon: "M23 6l-8 8-4-4-8 8" },
        {
            id: "alerts",
            label: "Alerts",
            icon: "M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0zM12 9v4M12 17h.01",
        },
        { id: "system", label: "System", icon: "M2 3h20v14H2zM8 21h8M12 17v4" },
        {
            id: "appearance",
            label: "Appearance",
            icon: "M12 1v2M12 21v2M1 12h2M21 12h2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10z",
        },
        {
            id: "about",
            label: "About",
            icon: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 14h-2v-2h2v2zm0-4h-2V7h2v5z",
        },
    ];

    function navigate(pageId) {
        activePage.set(pageId);
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
                    <svg
                        class="nav-icon"
                        width="16"
                        height="16"
                        viewBox="0 0 24 24"
                        fill="none"
                        aria-hidden="true"
                    >
                        <path
                            d={page.icon}
                            stroke="currentColor"
                            stroke-width="1.8"
                            stroke-linecap="round"
                            stroke-linejoin="round"
                        />
                    </svg>
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
        color: var(--color-primary);
    }
    .nav-item .nav-icon {
        flex-shrink: 0;
        opacity: 0.5;
        transition: opacity var(--transition);
    }
    .nav-item:hover .nav-icon {
        opacity: 0.75;
    }
    .nav-item.active .nav-icon {
        opacity: 1;
    }
</style>

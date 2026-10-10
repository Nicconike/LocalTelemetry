import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
    plugins: [svelte()],
    test: {
        environment: 'happy-dom',
        globals: true,
        server: {
            deps: {
                inline: [/lucide-svelte/],
            },
        },
    },
    resolve: {
        conditions: ['browser'],
        dedupe: ['svelte'],
    },
});

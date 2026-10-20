import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
    plugins: [svelte()],
    test: {
        environment: 'happy-dom',
        globals: true,
        include: ['src/**/*.test.ts'],
        coverage: {
            provider: 'v8',
            reporter: ['text', 'json', 'lcov', 'clover', 'cobertura'],
            reportsDirectory: './coverage',
            allowExternal: true,
        },
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

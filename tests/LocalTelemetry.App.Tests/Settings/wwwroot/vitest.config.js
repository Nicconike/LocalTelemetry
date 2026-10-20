import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));

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
        alias: {
            'lucide-svelte': resolve(__dirname, 'node_modules/lucide-svelte'),
            '@lucide/svelte': resolve(__dirname, 'node_modules/@lucide/svelte'),
        },
        conditions: ['browser'],
        dedupe: ['svelte'],
    },
});

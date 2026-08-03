import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import fs from 'node:fs';
import path from 'node:path';

function copyMasterAppIcon() {
    return {
        name: 'copy-master-app-icon',
        closeBundle() {
            const src = path.resolve(import.meta.dirname, '../../app.ico');
            const dest = path.resolve(import.meta.dirname, 'dist/app.ico');
            if (fs.existsSync(src)) {
                fs.copyFileSync(src, dest);
            }
        }
    };
}

export default defineConfig({
    plugins: [svelte(), copyMasterAppIcon()],
    base: './',
    build: {
        outDir: 'dist',
        emptyOutDir: true,
        rollupOptions: {
            input: {
                index: 'index.html',
            },
            output: {
                entryFileNames: 'assets/[name].js',
                chunkFileNames: 'assets/[name].js',
                assetFileNames: 'assets/[name].[ext]',
            },
        },
    },
    test: {
        environment: 'happy-dom',
        globals: true,
        include: ['../../../../tests/LocalTelemetry.App.Tests/Settings/wwwroot/**/*.test.ts'],
        coverage: {
            provider: 'v8',
            reporter: ['text', 'json', 'html'],
        },
    },
});

import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { codecovVitePlugin } from '@codecov/vite-plugin';
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
    plugins: [
        svelte(),
        copyMasterAppIcon(),
        codecovVitePlugin({
            enableBundleAnalysis: process.env.CODECOV_TOKEN !== undefined,
            bundleName: 'settings-ui',
            uploadToken: process.env.CODECOV_TOKEN,
            gitService: 'github',
        }),
    ],
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
    server: {
        fs: {
            allow: ['..', '../../../../tests/LocalTelemetry.App.Tests/Settings/wwwroot']
        }
    },
    test: {
        environment: 'happy-dom',
        globals: true,
        server: {
            deps: {
                inline: [/svelte/]
            }
        },
        include: ['../../../../tests/LocalTelemetry.App.Tests/Settings/wwwroot/**/*.test.ts'],
        coverage: {
            provider: 'v8',
            reporter: ['text', 'json', 'html'],
        },
    },
    resolve: {
        conditions: ['browser']
    }
});

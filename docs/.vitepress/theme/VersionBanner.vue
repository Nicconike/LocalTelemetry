<template>
    <div v-if="isOutdated && !isDismissed" class="version-banner" role="alert">
        <div class="version-banner-content">
            <div class="version-banner-icon" aria-hidden="true">
                <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none"
                    stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
                    <line x1="12" y1="9" x2="12" y2="13" />
                    <line x1="12" y1="17" x2="12.01" y2="17" />
                </svg>
            </div>
            <div class="version-banner-text">
                <span>You are viewing documentation for an older release (<strong>{{ versionTag }}</strong>). A newer
                    version is available.</span>
            </div>
            <div class="version-banner-actions">
                <a :href="latestUrl" class="version-banner-btn">
                    Switch to Latest Docs
                    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none"
                        stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M5 12h14" />
                        <path d="m12 5 7 7-7 7" />
                    </svg>
                </a>
                <button type="button" class="version-banner-close" aria-label="Dismiss banner" title="Dismiss banner"
                    @click="dismiss">
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none"
                        stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <line x1="18" y1="6" x2="6" y2="18" />
                        <line x1="6" y1="6" x2="18" y2="18" />
                    </svg>
                </button>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRoute, withBase } from 'vitepress';
import { latestReleaseTag } from '../versions.mjs';

const route = useRoute();
const isDismissed = ref(false);

const versionInfo = computed(() => {
    const path = route.path || '';
    // Match e.g. /v1.0.0/... or /LocalTelemetry/v1.0.0/...
    const match = path.match(/(?:^|\/)v(\d[\w.-]*)(?:\/(.*))?$/);
    if (!match) return null;

    const version = match[1];
    let subPath = match[2] || '';

    // Clean up any trailing index.html or .html for pretty URLs
    subPath = subPath.replace(/\/index\.html$/, '/').replace(/\.html$/, '');

    return {
        tag: `v${version}`,
        subPath
    };
});

// A release is only outdated if a newer release tag exists
const isOutdated = computed(() => {
    if (!versionInfo.value) return false;
    if (!latestReleaseTag) return false;
    return versionInfo.value.tag !== latestReleaseTag;
});
const versionTag = computed(() => versionInfo.value?.tag || '');

const latestUrl = computed(() => {
    if (!versionInfo.value) return withBase('/');
    const sub = versionInfo.value.subPath;
    return withBase(sub ? `/${sub}` : '/');
});

// Sync dismissal state with sessionStorage
watch(
    () => versionInfo.value?.tag,
    (newTag) => {
        if (typeof window === 'undefined' || !newTag) {
            isDismissed.value = false;
            return;
        }
        try {
            isDismissed.value = sessionStorage.getItem(`lt_dismissed_version_${newTag}`) === 'true';
        } catch {
            isDismissed.value = false;
        }
    },
    { immediate: true }
);

function dismiss() {
    isDismissed.value = true;
    if (typeof window !== 'undefined' && versionInfo.value?.tag) {
        try {
            sessionStorage.setItem(`lt_dismissed_version_${versionInfo.value.tag}`, 'true');
        } catch {
            // Ignore sessionStorage errors (e.g. private browsing restrictions)
        }
    }
}
</script>

<style scoped>
.version-banner {
    margin: 16px 0 24px 0;
    padding: 12px 16px;
    background: linear-gradient(135deg, rgba(234, 179, 8, 0.12) 0%, rgba(245, 158, 11, 0.08) 100%);
    border: 1px solid rgba(234, 179, 8, 0.35);
    border-radius: 8px;
    color: var(--vp-c-text-1);
    font-size: 14px;
    line-height: 1.5;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
    backdrop-filter: blur(4px);
}

.version-banner-content {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
}

.version-banner-icon {
    color: #eab308;
    display: flex;
    align-items: center;
    flex-shrink: 0;
}

.version-banner-text {
    flex: 1 1 260px;
}

.version-banner-text strong {
    color: #f59e0b;
    font-weight: 600;
}

.version-banner-actions {
    display: flex;
    align-items: center;
    gap: 10px;
    flex-shrink: 0;
}

.version-banner-btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 14px;
    background-color: #f59e0b;
    color: #000000 !important;
    font-weight: 600;
    font-size: 13px;
    border-radius: 6px;
    text-decoration: none !important;
    transition: all 0.2s ease;
    white-space: nowrap;
}

.version-banner-btn:hover {
    background-color: #9a3412;
    color: #ffffff !important;
    transform: translateY(-1px);
    box-shadow: 0 4px 12px rgba(245, 158, 11, 0.25);
}

.version-banner-close {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 28px;
    height: 28px;
    border-radius: 6px;
    color: var(--vp-c-text-2);
    background: transparent;
    border: none;
    cursor: pointer;
    transition: background-color 0.2s ease, color 0.2s ease;
}

.version-banner-close:hover {
    background-color: rgba(234, 179, 8, 0.2);
    color: var(--vp-c-text-1);
}

@media (max-width: 640px) {
    .version-banner-content {
        flex-direction: column;
        align-items: flex-start;
        gap: 10px;
    }

    .version-banner-actions {
        width: 100%;
        justify-content: space-between;
    }
}
</style>

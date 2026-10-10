<script setup lang="ts">
import { ref, watch } from 'vue';
import { useRoute, useData } from 'vitepress';

const route = useRoute();
const { page } = useData();

const feedbackState = ref<'idle' | 'submitted'>('idle');
const feedbackValue = ref<boolean | null>(null);

// Reset feedback when navigating between pages
watch(
    () => route.path,
    () => {
        feedbackState.value = 'idle';
        feedbackValue.value = null;
    }
);

function sendFeedback(helpful: boolean) {
    feedbackValue.value = helpful;
    feedbackState.value = 'submitted';

    if (typeof window !== 'undefined' && (window as any).gtag) {
        (window as any).gtag('event', 'page_feedback', {
            page_path: route.path,
            page_title: page.value.title || document.title,
            helpful: helpful ? 'yes' : 'no'
        });
    }
}
</script>

<template>
    <div class="feedback-container">
        <div v-if="feedbackState === 'idle'" class="feedback-prompt">
            <span class="feedback-label">Was this page helpful?</span>
            <div class="feedback-buttons">
                <button type="button" class="feedback-btn" aria-label="Vote Yes" @click="sendFeedback(true)">
                    <svg class="feedback-icon" viewBox="0 0 24 24" width="16" height="16" fill="none"
                        stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M7 10v12" />
                        <path
                            d="M15 5.88 14 10h5.83a2 2 0 0 1 1.92 2.56l-2.33 8A2 2 0 0 1 17.5 22H4a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2h2.76a2 2 0 0 0 1.79-1.11L12 2a3.13 3.13 0 0 1 3 3.88Z" />
                    </svg>
                    <span>Yes</span>
                </button>
                <button type="button" class="feedback-btn" aria-label="Vote No" @click="sendFeedback(false)">
                    <svg class="feedback-icon" viewBox="0 0 24 24" width="16" height="16" fill="none"
                        stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M17 14V2" />
                        <path
                            d="M9 18.12 10 14H4.17a2 2 0 0 1-1.92-2.56l2.33-8A2 2 0 0 1 6.5 2H20a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-2.76a2 2 0 0 0-1.79 1.11L12 22a3.13 3.13 0 0 1-3-3.88Z" />
                    </svg>
                    <span>No</span>
                </button>
            </div>
        </div>

        <div v-else class="feedback-submitted">
            <template v-if="feedbackValue">
                <span class="feedback-thanks">🎉 Thanks for your feedback!</span>
            </template>
            <template v-else>
                <span class="feedback-thanks">
                    Thanks for letting me know. You can
                    <a href="https://github.com/Nicconike/LocalTelemetry/issues/new" target="_blank" rel="noreferrer"
                        class="feedback-link">report an issue</a>
                    or suggest improvements on GitHub.
                </span>
            </template>
        </div>
    </div>
</template>

<style scoped>
.feedback-container {
    margin-top: 32px;
    padding: 16px 20px;
    border-radius: 8px;
    background: var(--vp-c-bg-alt);
    border: 1px solid var(--vp-c-border);
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    font-size: 14px;
}

.feedback-prompt {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    gap: 16px;
    flex-wrap: wrap;
}

.feedback-label {
    font-weight: 500;
    color: var(--vp-c-text-1);
}

.feedback-buttons {
    display: flex;
    align-items: center;
    gap: 8px;
}

.feedback-btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 14px;
    font-size: 13px;
    font-weight: 500;
    border-radius: 6px;
    background: var(--vp-c-bg);
    border: 1px solid var(--vp-c-border);
    color: var(--vp-c-text-1);
    cursor: pointer;
    transition: all 0.2s ease;
}

.feedback-btn:hover {
    border-color: var(--vp-c-brand-1);
    color: var(--vp-c-brand-1);
    background: var(--vp-c-brand-soft);
}

.feedback-icon {
    flex-shrink: 0;
}

.feedback-submitted {
    display: flex;
    align-items: center;
    width: 100%;
    color: var(--vp-c-text-2);
    line-height: 1.5;
}

.feedback-thanks {
    font-size: 14px;
    color: var(--vp-c-text-1);
}

.feedback-link {
    color: var(--vp-c-brand-1);
    text-decoration: underline;
    font-weight: 500;
}

.feedback-link:hover {
    text-decoration: none;
}
</style>

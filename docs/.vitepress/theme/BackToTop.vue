<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';

const isVisible = ref(false);

function handleScroll() {
    if (typeof window !== 'undefined') {
        isVisible.value = window.scrollY > 300;
    }
}

function scrollToTop() {
    if (typeof window !== 'undefined') {
        window.scrollTo({
            top: 0,
            behavior: 'smooth'
        });
    }
}

onMounted(() => {
    window.addEventListener('scroll', handleScroll, { passive: true });
    handleScroll();
});

onUnmounted(() => {
    window.removeEventListener('scroll', handleScroll);
});
</script>

<template>
    <Transition name="fade">
        <button v-if="isVisible" type="button" class="back-to-top-btn" title="Scroll to top" aria-label="Scroll to top"
            @click="scrollToTop">
            <svg class="back-to-top-icon" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor"
                stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="m18 15-6-6-6 6" />
            </svg>
        </button>
    </Transition>
</template>

<style scoped>
.back-to-top-btn {
    position: fixed;
    bottom: 28px;
    right: 28px;
    z-index: 50;
    width: 40px;
    height: 40px;
    border-radius: 50%;
    background: var(--vp-c-bg-elv);
    border: 1px solid var(--vp-c-border);
    color: var(--vp-c-text-2);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.25);
    transition: all 0.25s ease;
    backdrop-filter: blur(8px);
}

.back-to-top-btn:hover {
    color: var(--vp-c-brand-1);
    border-color: rgba(96, 165, 250, 0.6);
    transform: translateY(-3px);
    box-shadow: 0 8px 24px rgba(56, 189, 248, 0.25);
}

.back-to-top-btn:active {
    transform: translateY(-1px);
}

.back-to-top-icon {
    transition: transform 0.2s ease;
}

.back-to-top-btn:hover .back-to-top-icon {
    transform: translateY(-1px);
}

.fade-enter-active,
.fade-leave-active {
    transition: opacity 0.25s ease, transform 0.25s ease;
}

.fade-enter-from,
.fade-leave-to {
    opacity: 0;
    transform: translateY(12px) scale(0.9);
}

@media (max-width: 768px) {
    .back-to-top-btn {
        bottom: 20px;
        right: 20px;
        width: 36px;
        height: 36px;
    }
}
</style>

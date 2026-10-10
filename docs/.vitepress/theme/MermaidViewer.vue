<template>
    <!-- Fullscreen Lightbox Modal -->
    <Teleport to="body">
        <Transition name="fade">
            <dialog v-if="isOpen" open class="mermaid-modal-backdrop" aria-label="Mermaid Diagram Viewer"
                @click.self="closeModal">
                <!-- Modal Top Toolbar -->
                <div class="mermaid-modal-header">
                    <div class="mermaid-modal-title">
                        <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none"
                            stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <rect width="18" height="18" x="3" y="3" rx="2" ry="2" />
                            <circle cx="9" cy="9" r="2" />
                            <path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21" />
                        </svg>
                        <span>Interactive Diagram Viewer</span>
                        <span class="mermaid-zoom-badge">{{ Math.round(scale * 100) }}%</span>
                    </div>

                    <div class="mermaid-modal-controls">
                        <button type="button" class="modal-btn" title="Zoom In (or Scroll Up)" aria-label="Zoom In"
                            @click="zoomIn">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
                                fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
                                stroke-linejoin="round">
                                <circle cx="11" cy="11" r="8" />
                                <line x1="21" y1="21" x2="16.65" y2="16.65" />
                                <line x1="11" y1="8" x2="11" y2="14" />
                                <line x1="8" y1="11" x2="14" y2="11" />
                            </svg>
                        </button>

                        <button type="button" class="modal-btn" title="Zoom Out (or Scroll Down)" aria-label="Zoom Out"
                            @click="zoomOut">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
                                fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
                                stroke-linejoin="round">
                                <circle cx="11" cy="11" r="8" />
                                <line x1="21" y1="21" x2="16.65" y2="16.65" />
                                <line x1="8" y1="11" x2="14" y2="11" />
                            </svg>
                        </button>

                        <button type="button" class="modal-btn" title="Reset View (Double-click canvas)"
                            aria-label="Reset View" @click="resetView">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
                                fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
                                stroke-linejoin="round">
                                <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
                                <path d="M3 3v5h5" />
                            </svg>
                        </button>

                        <div class="modal-divider"></div>

                        <button type="button" class="modal-btn" title="Open in New Tab" aria-label="Open in New Tab"
                            @click="openInNewTab">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
                                fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
                                stroke-linejoin="round">
                                <path d="M15 3h6v6" />
                                <path d="M10 14 21 3" />
                                <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
                            </svg>
                        </button>

                        <button type="button" class="modal-btn" title="Download SVG" aria-label="Download SVG"
                            @click="downloadSvg">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
                                fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
                                stroke-linejoin="round">
                                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                                <polyline points="7 10 12 15 17 10" />
                                <line x1="12" y1="15" x2="12" y2="3" />
                            </svg>
                        </button>

                        <button type="button" class="modal-btn modal-close" title="Close (Esc)"
                            aria-label="Close Viewer" @click="closeModal">
                            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24"
                                fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
                                stroke-linejoin="round">
                                <line x1="18" y1="6" x2="6" y2="18" />
                                <line x1="6" y1="6" x2="18" y2="18" />
                            </svg>
                        </button>
                    </div>
                </div>

                <!-- Canvas Body (Pan & Zoom) -->
                <div ref="canvasRef" class="mermaid-modal-canvas" :class="{ dragging: isDragging }"
                    @wheel.prevent="handleWheel" @pointerdown="handlePointerDown" @pointermove="handlePointerMove"
                    @pointerup="handlePointerUp" @pointercancel="handlePointerUp" @dblclick="resetView">
                    <div class="mermaid-modal-content" :style="{
                        transform: `translate(${panX}px, ${panY}px) scale(${scale})`,
                        transformOrigin: 'center center'
                    }" v-html="currentSvgHtml"></div>
                </div>

                <!-- Bottom Helper Hint -->
                <div class="mermaid-modal-footer">
                    <span>Scroll to zoom &bull; Click &amp; drag to pan &bull; Double-click to reset &bull; Press Esc to
                        exit</span>
                </div>
            </dialog>
        </Transition>
    </Teleport>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, nextTick } from 'vue';
import { useRoute } from 'vitepress';

const route = useRoute();
const isOpen = ref(false);
const currentSvgHtml = ref('');
const scale = ref(1);
const panX = ref(0);
const panY = ref(0);
const isDragging = ref(false);
const dragStart = { x: 0, y: 0 };
const canvasRef = ref<HTMLElement | null>(null);

let observer: MutationObserver | null = null;

function openModal(svgHtml: string) {
    currentSvgHtml.value = svgHtml;
    scale.value = 1;
    panX.value = 0;
    panY.value = 0;
    isOpen.value = true;
    if (typeof document !== 'undefined') {
        document.body.style.overflow = 'hidden';
    }
}

function closeModal() {
    isOpen.value = false;
    currentSvgHtml.value = '';
    if (typeof document !== 'undefined') {
        document.body.style.overflow = '';
    }
}

function zoomIn() {
    scale.value = Math.min(scale.value * 1.25, 5);
}

function zoomOut() {
    scale.value = Math.max(scale.value / 1.25, 0.2);
}

function resetView() {
    scale.value = 1;
    panX.value = 0;
    panY.value = 0;
}

function handleWheel(e: WheelEvent) {
    const zoomFactor = e.deltaY < 0 ? 1.15 : 0.87;
    const newScale = Math.min(Math.max(scale.value * zoomFactor, 0.2), 5);
    scale.value = newScale;
}

function handlePointerDown(e: PointerEvent) {
    if (e.button !== 0) return; // Primary button only
    isDragging.value = true;
    dragStart.x = e.clientX - panX.value;
    dragStart.y = e.clientY - panY.value;
    (e.currentTarget as HTMLElement)?.setPointerCapture(e.pointerId);
}

function handlePointerMove(e: PointerEvent) {
    if (!isDragging.value) return;
    panX.value = e.clientX - dragStart.x;
    panY.value = e.clientY - dragStart.y;
}

function handlePointerUp(e: PointerEvent) {
    if (isDragging.value) {
        isDragging.value = false;
        try {
            (e.currentTarget as HTMLElement)?.releasePointerCapture(e.pointerId);
        } catch {
            // Ignored
        }
    }
}

function generateStandaloneHtml(svgHtml: string): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>LocalTelemetry Architecture Diagram</title>
  <style>
    :root {
      color-scheme: dark;
    }
    * {
      box-sizing: border-box;
    }
    body {
      margin: 0;
      padding: 32px;
      min-height: 100vh;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      background: #090a0f;
      color: #f4f4f5;
      font-family: 'Inter', system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      -webkit-font-smoothing: antialiased;
    }
    .container {
      width: 100%;
      max-width: 1600px;
      display: flex;
      justify-content: center;
      overflow: auto;
      padding: 40px 28px;
      background: #090a0f;
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 12px;
      box-shadow: 0 8px 32px rgba(0, 0, 0, 0.5);
    }
    svg {
      max-width: 100%;
      height: auto;
      display: block;
      margin: 0 auto;
      overflow: visible;
      filter: drop-shadow(0 2px 8px rgba(0, 0, 0, 0.35));
    }
    /* Node Typography & Resets */
    .node foreignObject {
      overflow: visible !important;
    }
    .node foreignObject > div {
      overflow: visible !important;
      line-height: 1.3 !important;
      text-align: center !important;
    }
    .node p,
    .node span,
    .node div,
    .node * {
      margin: 0 !important;
      padding: 0 !important;
      line-height: 1.3 !important;
      font-family: 'Inter', system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif !important;
    }
    /* Edge Labels: Seamless text along arrows */
    g.edgeLabel foreignObject {
      overflow: visible !important;
    }
    g.edgeLabel,
    g.edgeLabel span,
    g.edgeLabel p {
      background: transparent !important;
      background-color: transparent !important;
    }
    g.edgeLabel .labelBkg {
      background: #090a0f !important;
      background-color: #090a0f !important;
      fill: #090a0f !important;
      border: none !important;
      outline: none !important;
      box-shadow: none !important;
      border-radius: 4px !important;
      padding: 1px 6px !important;
    }
    g.edgeLabel p,
    g.edgeLabel span {
      color: #94a3b8 !important;
      font-family: 'Inter', system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif !important;
      font-size: 11px !important;
      font-weight: 500 !important;
      line-height: 1.25 !important;
      margin: 0 !important;
      padding: 0 !important;
    }
  </style>
</head>
<body>
  <div class="container mermaid-standalone">
    ${svgHtml}
  </div>
</body>
</html>`;
}

function openInNewTab() {
    if (!currentSvgHtml.value) return;
    const html = generateStandaloneHtml(currentSvgHtml.value);
    const blob = new Blob([html], { type: 'text/html;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    window.open(url, '_blank');
}

function downloadSvg() {
    if (!currentSvgHtml.value) return;
    const blob = new Blob([currentSvgHtml.value], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `localtelemetry-diagram-${Date.now()}.svg`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}

function handleKeydown(e: KeyboardEvent) {
    if (!isOpen.value) return;
    if (e.key === 'Escape') closeModal();
    else if (e.key === '+' || e.key === '=') zoomIn();
    else if (e.key === '-' || e.key === '_') zoomOut();
    else if (e.key === '0') resetView();
}

/**
 * Scan for and enhance .mermaid containers with interactive controls & lightbox triggers.
 */
function enhanceMermaidContainers() {
    if (typeof document === 'undefined') return;

    const containers = document.querySelectorAll<HTMLElement>('.mermaid');
    containers.forEach((container) => {
        // If toolbar is already attached and present, skip
        if (container.querySelector('.mermaid-action-toolbar')) return;

        // Ensure the SVG is rendered inside before attaching controls
        const svg = container.querySelector('svg');
        if (!svg) return;

        container.classList.add('mermaid-interactive');

        // Create action toolbar
        const toolbar = document.createElement('div');
        toolbar.className = 'mermaid-action-toolbar';

        // Button: Zoom / Lightbox
        const expandBtn = document.createElement('button');
        expandBtn.type = 'button';
        expandBtn.className = 'mermaid-tool-btn';
        expandBtn.title = 'Open interactive fullscreen lightbox';
        expandBtn.setAttribute('aria-label', 'Open lightbox');
        expandBtn.innerHTML = `
      <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M15 3h6v6"/>
        <path d="m21 3-7 7"/>
        <path d="M9 21H3v-6"/>
        <path d="m3 21 7-7"/>
      </svg>
      <span>Enlarge</span>
    `;
        expandBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const liveSvg = container.querySelector<SVGElement>(':scope > svg');
            if (liveSvg) openModal(liveSvg.outerHTML);
        });

        // Button: Open in New Tab
        const tabBtn = document.createElement('button');
        tabBtn.type = 'button';
        tabBtn.className = 'mermaid-tool-btn';
        tabBtn.title = 'Open diagram in separate tab';
        tabBtn.setAttribute('aria-label', 'Open in new tab');
        tabBtn.innerHTML = `
      <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M15 3h6v6"/>
        <path d="M10 14 21 3"/>
        <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>
      </svg>
      <span>New Tab</span>
    `;
        tabBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const liveSvg = container.querySelector<SVGElement>(':scope > svg');
            if (!liveSvg) return;
            const html = generateStandaloneHtml(liveSvg.outerHTML);
            const blob = new Blob([html], { type: 'text/html;charset=utf-8' });
            const url = URL.createObjectURL(blob);
            window.open(url, '_blank');
        });

        toolbar.appendChild(expandBtn);
        toolbar.appendChild(tabBtn);
        container.appendChild(toolbar);

        // Clicking the diagram directly opens the lightbox
        if (container.dataset.clickBound !== 'true') {
            container.dataset.clickBound = 'true';
            container.addEventListener('click', (e) => {
                const target = e.target as HTMLElement;
                if (target.closest('.mermaid-action-toolbar')) return;
                const liveSvg = container.querySelector<SVGElement>(':scope > svg');
                if (liveSvg) openModal(liveSvg.outerHTML);
            });
        }
    });
}

onMounted(() => {
    window.addEventListener('keydown', handleKeydown);

    // Initial scan
    nextTick(() => {
        enhanceMermaidContainers();
    });

    // Observe DOM for subsequent renders or client-side navigation
    observer = new MutationObserver(() => {
        enhanceMermaidContainers();
    });

    observer.observe(document.body, {
        childList: true,
        subtree: true
    });
});

onUnmounted(() => {
    if (typeof window !== 'undefined') {
        window.removeEventListener('keydown', handleKeydown);
    }
    if (observer) {
        observer.disconnect();
        observer = null;
    }
    if (typeof document !== 'undefined') {
        document.body.style.overflow = '';
    }
});
</script>

<style>
/* Mermaid Diagram Container Custom Enhancements */
.mermaid.mermaid-interactive {
    position: relative !important;
    cursor: zoom-in;
    transition: border-color 0.2s ease, box-shadow 0.2s ease;
}

.mermaid.mermaid-interactive:hover {
    border-color: rgba(56, 189, 248, 0.4) !important;
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25);
}

.mermaid-action-toolbar {
    position: absolute;
    top: 10px;
    right: 12px;
    display: flex;
    align-items: center;
    gap: 6px;
    opacity: 0.9;
    transition: opacity 0.2s ease, transform 0.2s ease;
    z-index: 20;
    background: rgba(18, 18, 22, 0.9);
    border: 1px solid rgba(255, 255, 255, 0.12);
    border-radius: 8px;
    padding: 3px 5px;
    backdrop-filter: blur(8px);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}

.mermaid.mermaid-interactive:hover .mermaid-action-toolbar {
    opacity: 1;
}

.mermaid-tool-btn {
    display: inline-flex !important;
    align-items: center !important;
    justify-content: center !important;
    gap: 5px !important;
    height: 24px !important;
    padding: 0 8px !important;
    font-size: 11px !important;
    font-weight: 500 !important;
    color: #e4e4e7 !important;
    background: transparent !important;
    border: 1px solid transparent !important;
    border-radius: 5px !important;
    cursor: pointer !important;
    line-height: 1 !important;
    white-space: nowrap !important;
    transition: all 0.15s ease !important;
}

.mermaid-tool-btn:hover {
    background: #0f172a !important;
    border-color: rgba(56, 189, 248, 0.3) !important;
    color: #38bdf8 !important;
}

.mermaid-tool-btn svg {
    width: 13px !important;
    height: 13px !important;
    min-width: 13px !important;
    max-width: 13px !important;
    min-height: 13px !important;
    max-height: 13px !important;
    display: inline-block !important;
    flex-shrink: 0 !important;
    stroke-width: 2 !important;
    margin: 0 !important;
    padding: 0 !important;
    box-sizing: content-box !important;
}

/* Fullscreen Lightbox Modal */
dialog.mermaid-modal-backdrop,
.mermaid-modal-backdrop {
    position: fixed;
    inset: 0;
    z-index: 9999;
    border: none;
    padding: 0;
    margin: 0;
    width: 100vw;
    height: 100vh;
    max-width: 100vw;
    max-height: 100vh;
    background-color: rgba(9, 10, 15, 0.96);
    backdrop-filter: blur(10px);
    display: flex;
    flex-direction: column;
    font-family: 'Inter', system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    -webkit-font-smoothing: antialiased;
}

.mermaid-modal-header {
    height: 56px;
    padding: 0 20px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    border-bottom: 1px solid rgba(255, 255, 255, 0.1);
    background: rgba(18, 18, 21, 0.7);
    backdrop-filter: blur(8px);
    flex-shrink: 0;
}

.mermaid-modal-title {
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 14px;
    font-weight: 600;
    color: #f4f4f5;
}

.mermaid-zoom-badge {
    display: inline-block;
    padding: 2px 8px;
    font-size: 11px;
    font-family: var(--vp-font-family-mono);
    background: #18181b;
    border: 1px solid #3f3f46;
    border-radius: 12px;
    color: #38bdf8;
}

.mermaid-modal-controls {
    display: flex;
    align-items: center;
    gap: 6px;
}

.modal-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 34px;
    height: 34px;
    border-radius: 6px;
    background: #27272a;
    border: 1px solid #3f3f46;
    color: #e4e4e7;
    cursor: pointer;
    transition: all 0.15s ease;
}

.modal-btn:hover {
    background: #0284c7;
    border-color: #38bdf8;
    color: #ffffff;
}

.modal-btn.modal-close:hover {
    background: #b91c1c;
    border-color: #ef4444;
    color: #ffffff;
}

.modal-divider {
    width: 1px;
    height: 20px;
    background: rgba(255, 255, 255, 0.12);
    margin: 0 4px;
}

.mermaid-modal-canvas {
    flex: 1;
    overflow: hidden;
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: grab;
    user-select: none;
    touch-action: none;
}

.mermaid-modal-canvas.dragging {
    cursor: grabbing;
}

.mermaid-modal-content {
    display: flex;
    align-items: center;
    justify-content: center;
    transition: transform 0.05s linear;
    max-width: 90vw;
    max-height: 80vh;
    background: transparent !important;
    border: none !important;
    box-shadow: none !important;
    padding: 0 !important;
    margin: 0 !important;
}

.mermaid-modal-content svg {
    max-width: none !important;
    height: auto !important;
    filter: drop-shadow(0 8px 28px rgba(0, 0, 0, 0.6));
}

.mermaid-modal-footer {
    height: 36px;
    padding: 0 16px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #18181b;
    border-top: 1px solid #27272a;
    font-size: 12px;
    color: #a1a1aa;
    flex-shrink: 0;
}

/* Vue Transitions */
.fade-enter-active,
.fade-leave-active {
    transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
    opacity: 0;
}
</style>

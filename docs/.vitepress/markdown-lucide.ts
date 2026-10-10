import lucideData from '@iconify-json/lucide/icons.json' with { type: 'json' };

const icons = lucideData.icons as Record<string, { body: string }>;

const ltMonochromeSvg = `<svg class="vp-lucide-icon" viewBox="0 0 24 24" width="1.15em" height="1.15em" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" aria-hidden="true"><path d="M11.5 3H5.5C4.12 3 3 4.12 3 5.5V9.5"/><path d="M3 14.5V18.5C3 19.88 4.12 21 5.5 21H11.5"/><path d="M12.5 3H18.5C19.88 3 21 4.12 21 5.5V7"/><path d="M12.5 20H18.5C19.33 20 20 19.33 20 18.5V13.844L22 12.5V18.5C22 20.43 20.43 22 18.5 22H12.5V20Z" fill="currentColor" stroke="none"/><path d="M1 12H5.7619L7.19048 9.2L8.61905 13.6L10.5238 6L12.4286 18L14.3333 13.2H16.2381L21 10" stroke-linecap="round"/><path d="M24 7.5L19 8.5L22 12L24 7.5Z" fill="currentColor" stroke="none"/></svg>`;

const ltColorSvg = `<svg class="vp-lucide-icon" viewBox="0 0 24 24" width="1.15em" height="1.15em" fill="none" stroke-width="2" stroke-linejoin="round" aria-hidden="true" style="vertical-align: -0.15em;"><path d="M11.5 3H5.5C4.12 3 3 4.12 3 5.5V9.5" stroke="#00E5FF"/><path d="M3 14.5V18.5C3 19.88 4.12 21 5.5 21H11.5" stroke="#00E5FF"/><path d="M12.5 3H18.5C19.88 3 21 4.12 21 5.5V7" stroke="#E2E8F0"/><path d="M12.5 20H18.5C19.33 20 20 19.33 20 18.5V13.844L22 12.5V18.5C22 20.43 20.43 22 18.5 22H12.5V20Z" fill="#E2E8F0" stroke="none"/><path d="M1 12H5.7619L7.19048 9.2L8.61905 13.6L10.5238 6L12.4286 18L14.3333 13.2H16.2381L21 10" stroke="#00E5FF" stroke-linecap="round"/><path d="M24 7.5L19 8.5L22 12L24 7.5Z" fill="#00E5FF" stroke="none"/></svg>`;

const customAppIcons: Record<string, string> = {
    // Lucide-style monochrome stroke icon (inherits currentColor)
    'localtelemetry': ltMonochromeSvg,
    'local-telemetry': ltMonochromeSvg,
    'app-icon': ltMonochromeSvg,
    'lt-icon': ltMonochromeSvg,
    'lt': ltMonochromeSvg,

    // Full-color branded app icon
    'app-logo': ltColorSvg,
    'app-color': ltColorSvg,
    'lt-logo': ltColorSvg,
    'localtelemetry-color': ltColorSvg
};

const iconAliases: Record<string, string> = {
    'user-group': 'users',
    'user-round-users': 'users-round',
};

export function renderLucideSvg(name: string): string | null {
    if (!name) return null;
    const rawClean = name.toLowerCase().trim();
    const clean = iconAliases[rawClean] || rawClean;

    // Check custom app icon shortcodes first
    if (customAppIcons[clean]) {
        return customAppIcons[clean];
    }

    // Look up directly from official Lucide icon dictionary (1,843 icons)
    const icon = icons[clean] || icons[clean.replaceAll('_', '-')];
    if (!icon) return null;
    return `<svg class="vp-lucide-icon" viewBox="0 0 24 24" width="1.15em" height="1.15em" aria-hidden="true">${icon.body}</svg>`;
}

const ESCAPED_SINGLE_QUOTE = String.raw`\'`;

export function lucideFrontmatterPlugin() {
    return {
        name: 'vitepress-lucide-frontmatter',
        enforce: 'pre' as const,
        transform(code: string, id: string) {
            if (!id.endsWith('.md')) return null;
            if (code.startsWith('---')) {
                const endIdx = code.indexOf('---', 3);
                if (endIdx !== -1) {
                    const frontmatter = code.slice(0, endIdx);
                    const transformedFm = frontmatter.replace(/icon:\s*['"]?:([a-z0-9-_]+):['"]?/g, (match, iconName) => {
                        const svg = renderLucideSvg(iconName);
                        if (!svg) return match;
                        const escapedSvg = svg.replaceAll("'", ESCAPED_SINGLE_QUOTE);
                        return `icon: '${escapedSvg}'`;
                    });
                    return transformedFm + code.slice(endIdx);
                }
            }
            return null;
        }
    };
}

function processTextShortcodes(child: any, state: any): any[] {
    const regex = /:([a-z0-9-]+):/g;
    let lastIdx = 0;
    let match: RegExpExecArray | null;
    const text: string = child.content;
    const tokens: any[] = [];

    while ((match = regex.exec(text)) !== null) {
        const iconName = match[1];
        const svg = renderLucideSvg(iconName);
        if (!svg) continue;

        if (match.index > lastIdx) {
            const textToken = new state.Token('text', '', 0);
            textToken.content = text.slice(lastIdx, match.index);
            tokens.push(textToken);
        }

        const htmlToken = new state.Token('html_inline', '', 0);
        htmlToken.content = svg;
        tokens.push(htmlToken);

        lastIdx = regex.lastIndex;
    }

    if (lastIdx === 0) {
        return [child];
    }
    if (lastIdx < text.length) {
        const afterToken = new state.Token('text', '', 0);
        afterToken.content = text.slice(lastIdx);
        tokens.push(afterToken);
    }
    return tokens;
}

function processEmojiToken(child: any, state: any): any {
    const svg = renderLucideSvg(child.markup);
    if (!svg) return child;

    const htmlToken = new state.Token('html_inline', '', 0);
    htmlToken.content = svg;
    return htmlToken;
}

function processChildToken(child: any, state: any): any[] {
    if (child.type === 'emoji') {
        return [processEmojiToken(child, state)];
    }
    if (child.type === 'text') {
        return processTextShortcodes(child, state);
    }
    return [child];
}

function processInlineToken(token: any, state: any): void {
    if (token.type !== 'inline' || !token.children) return;

    const newChildren: any[] = [];
    for (const child of token.children) {
        newChildren.push(...processChildToken(child, state));
    }
    token.children = newChildren;
}

export function lucideMarkdownPlugin(md: any) {
    // 1. Override markdown-it-emoji renderer so emoji tokens become Lucide SVGs
    md.renderer.rules.emoji = (tokens: any[], idx: number) => {
        const markup = tokens[idx].markup;
        const svg = renderLucideSvg(markup);
        return svg || tokens[idx].content;
    };

    // 2. Process inline tokens in the core ruler
    md.core.ruler.push('lucide_icons', (state: any) => {
        for (const token of state.tokens) {
            processInlineToken(token, state);
        }
    });
}

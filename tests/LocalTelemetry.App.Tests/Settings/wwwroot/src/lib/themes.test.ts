import { themes, applyThemeVars } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/themes';

describe('themes.ts', () => {
    it('should contain predefined theme definitions', () => {
        expect(themes).toBeDefined();
        expect(themes.length).toBeGreaterThan(0);
    });

    it('every theme should have value, label and CSS variables', () => {
        for (const theme of themes) {
            expect(theme.value).toBeTruthy();
            expect(theme.label).toBeTruthy();
            expect(theme.vars).toBeDefined();
            expect(theme.vars['--color-bg']).toBeTruthy();
            expect(theme.vars['--color-surface']).toBeTruthy();
            expect(theme.vars['--color-text']).toBeTruthy();
        }
    });

    it('should include default themes', () => {
        const defaultDark = themes.find((t) => t.value === 'default');
        expect(defaultDark).toBeDefined();
        expect(defaultDark?.label).toBe('Default Dark');

        const defaultLight = themes.find((t) => t.value === 'default-light');
        expect(defaultLight).toBeDefined();
        expect(defaultLight?.label).toBe('Default Light');
    });

    it('should include new OLED, Nord, and Cyberpunk themes', () => {
        const expectedThemes = [
            { value: 'oled', label: 'OLED Black' },
            { value: 'oled-light', label: 'OLED Light' },
            { value: 'nord', label: 'Nord' },
            { value: 'nord-light', label: 'Nord Light' },
            { value: 'cyberpunk', label: 'Cyberpunk' },
            { value: 'cyberpunk-light', label: 'Cyberpunk Light' }
        ];

        for (const exp of expectedThemes) {
            const found = themes.find((t) => t.value === exp.value);
            expect(found).toBeDefined();
            expect(found?.label).toBe(exp.label);
            expect(found?.vars['--color-bg']).toBeTruthy();
            expect(found?.vars['--color-surface']).toBeTruthy();
            expect(found?.vars['--color-primary']).toBeTruthy();
        }

        applyThemeVars('oled');
        expect(document.documentElement.style.getPropertyValue('--color-bg')).toBe('#000000');
        expect(document.documentElement.dataset.theme).toBe('dark');

        applyThemeVars('nord');
        expect(document.documentElement.style.getPropertyValue('--color-bg')).toBe('#242933');
        expect(document.documentElement.dataset.theme).toBe('dark');

        applyThemeVars('cyberpunk');
        expect(document.documentElement.style.getPropertyValue('--color-bg')).toBe('#09090b');
        expect(document.documentElement.dataset.theme).toBe('dark');
    });

    it('applyThemeVars should apply CSS variables to document.documentElement', () => {
        applyThemeVars('midnight');
        const bg = document.documentElement.style.getPropertyValue('--color-bg');
        expect(bg).toBe('#0a0e17');
        expect(document.documentElement.dataset.theme).toBe('dark');

        applyThemeVars('default-light');
        const lightBg = document.documentElement.style.getPropertyValue('--color-bg');
        expect(lightBg).toBe('#f5f4f0');
        expect(document.documentElement.dataset.theme).toBe('light');

        applyThemeVars('unknown_theme_xyz');
        const fallbackBg = document.documentElement.style.getPropertyValue('--color-bg');
        expect(fallbackBg).toBe('#171614');
        expect(document.documentElement.dataset.theme).toBe('dark');

        // Test theme with missing variables to cover default fallback branch
        themes.push({
            value: 'partial-theme',
            label: 'Partial Theme',
            vars: {} as any
        });
        applyThemeVars('partial-theme');
        expect(document.documentElement.style.getPropertyValue('--color-bg')).toBe('#171614');
        themes.pop();
    });

    it('every theme should define brand and primary colors', () => {
        for (const theme of themes) {
            expect(theme.vars['--color-brand']).toBeTruthy();
            expect(theme.vars['--color-primary']).toBeTruthy();
        }
        const defaultDark = themes.find((t) => t.value === 'default');
        expect(defaultDark?.vars['--color-primary']).toBe('#0099b8');
        expect(defaultDark?.vars['--color-brand']).toBe('#0099b8');
    });

    it('every theme should have unique primary and secondary accent colors without duplicates', () => {
        const primaries = themes.map((t) => t.vars['--color-primary']);
        const secondaries = themes.map((t) => t.vars['--color-secondary']);

        expect(new Set(primaries).size).toBe(themes.length);
        expect(new Set(secondaries).size).toBe(themes.length);

        const oled = themes.find((t) => t.value === 'oled');
        expect(oled?.vars['--color-primary']).toBe('#e11d48');
        expect(oled?.vars['--color-secondary']).toBe('#fb7185');
    });
});

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

    it('applyThemeVars should apply CSS variables to document.documentElement', () => {
        applyThemeVars('midnight');
        const bg = document.documentElement.style.getPropertyValue('--color-bg');
        expect(bg).toBe('#0d1117');

        applyThemeVars('unknown_theme_xyz');
        const fallbackBg = document.documentElement.style.getPropertyValue('--color-bg');
        expect(fallbackBg).toBe('#171614');
    });
});

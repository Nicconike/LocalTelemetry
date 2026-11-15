declare module '@semantic-release/release-notes-generator' {
    export function generateNotes(pluginConfig: any, context: any): Promise<string>;
}

declare module '@semantic-release/release-notes-generator' {
    export function generateNotes(pluginConfig: any, context: any): Promise<string>;
}

declare module '@semantic-release/github' {
    export function verifyConditions(pluginConfig: any, context: any): Promise<void>;
    export function publish(pluginConfig: any, context: any): Promise<any>;
    export function addChannel(pluginConfig: any, context: any): Promise<any>;
    export function success(pluginConfig: any, context: any): Promise<void>;
    export function fail(pluginConfig: any, context: any): Promise<void>;
}

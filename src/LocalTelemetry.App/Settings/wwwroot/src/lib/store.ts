import { writable } from 'svelte/store';
import type { Writable } from 'svelte/store';
import type { SettingsDto, SystemInfo, TrafficDay, TrafficTodayPayload } from "./types.ts";

export const settings: Writable<SettingsDto | null> = writable(null);
export const pristineSettings: Writable<string> = writable('');

export const saveStatus: Writable<'idle' | 'saving' | 'saved' | 'error'> = writable('idle');

export const nics: Writable<string[]> = writable([]);

export const activePage: Writable<string> = writable('general');

export const allTrafficHistory: Writable<TrafficDay[]> = writable([]);

export const trafficHistory: Writable<TrafficDay[]> = writable([]);

export const trafficToday: Writable<TrafficTodayPayload> = writable({ downBytes: 0, upBytes: 0 });

export const trafficMonths: Writable<string[]> = writable([]);

export const trafficCache: Map<string, TrafficDay[]> = new Map();

export const importResult: Writable<{ daysImported: number; error?: string } | null> = writable(null);

export const systemInfo: Writable<SystemInfo> = writable({} as SystemInfo);

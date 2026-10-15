<script setup lang="ts">
import { computed } from 'vue';
import { useRoute, useData } from 'vitepress';
import VPFlyout from 'vitepress/dist/client/theme-default/components/VPFlyout.vue';
import VPNavScreenMenuGroup from 'vitepress/dist/client/theme-default/components/VPNavScreenMenuGroup.vue';
import { versions } from '../versions.mjs';

defineProps<{
    screenMenu?: boolean;
}>();

const route = useRoute();
const { site } = useData();

const parsedVersion = computed(() => {
    let path = route.path || '/';
    const baseUrl = site.value?.base || '/';
    if (baseUrl !== '/' && path.startsWith(baseUrl)) {
        path = '/' + path.slice(baseUrl.length);
    }

    // Match version prefix like /v1.0.0-rc.1/...
    const match = path.match(/^\/v(\d[\w.-]*)(?:\/(.*))?$/);
    if (match) {
        let sub = match[2] || '';
        sub = sub.replace(/\/index\.html$/, '/').replace(/\.html$/, '');
        return {
            tag: `v${match[1]}`,
            subPath: sub
        };
    }

    let sub = path.replace(/^\//, '');
    sub = sub.replace(/\/index\.html$/, '/').replace(/\.html$/, '');
    return {
        tag: 'Latest',
        subPath: sub
    };
});

const currentLabel = computed(() => parsedVersion.value.tag);
const isNonLatest = computed(() => parsedVersion.value.tag !== 'Latest');

const menuItems = computed(() => {
    const sub = parsedVersion.value.subPath;
    const baseUrl = site.value?.base || '/';
    const basePrefix = baseUrl !== '/' ? baseUrl.replace(/^\/|\/$/g, '') + '/' : '';

    const versionLinks = versions.map((v) => {
        const isLatest = v.text === 'Latest';
        let targetLink = '';
        let activeMatch = '';

        if (isLatest) {
            targetLink = sub ? `/${sub}` : '/';
            // Matches any route that does not have a version prefix (e.g. /user-guide/... or /)
            activeMatch = String.raw`^(?:/${basePrefix})?(?!v\d)`;
        } else {
            const tag = v.text;
            targetLink = sub ? `/${tag}/${sub}` : `/${tag}/`;
            const escapedTag = tag.replaceAll('.', String.raw`\.`);
            activeMatch = `(?:^|/)${escapedTag}(?:/|$)`;
        }

        return {
            text: v.text,
            link: targetLink,
            activeMatch
        };
    });

    return [
        ...versionLinks,
        {
            items: [
                { text: 'Changelog', link: 'https://github.com/Nicconike/LocalTelemetry/blob/master/CHANGELOG.md' },
                { text: 'App Releases', link: 'https://github.com/Nicconike/LocalTelemetry/releases' }
            ]
        }
    ];
});
</script>

<template>
    <VPNavScreenMenuGroup v-if="screenMenu" :text="currentLabel" :items="menuItems" />
    <VPFlyout v-else class="VPNavBarMenuGroup" :class="{ active: isNonLatest }" :button="currentLabel"
        :items="menuItems" />
</template>

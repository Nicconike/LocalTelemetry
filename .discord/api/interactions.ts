import crypto from 'node:crypto';
import { Buffer } from 'node:buffer';
import process from 'node:process';

const GITHUB_REPO = 'Nicconike/LocalTelemetry';

interface VercelLikeRequest {
    method?: string;
    headers: Record<string, string | string[] | undefined>;
    body: unknown;
}

interface VercelLikeResponse {
    status: (code: number) => VercelLikeResponse;
    json: (data: unknown) => void;
    send: (body: string) => void;
}

interface DiscordInteraction {
    id: string;
    type: number;
    data?: {
        name: string;
        id: string;
    };
    token: string;
}

interface ReleaseAsset {
    name: string;
    browser_download_url: string;
}

interface GitHubRelease {
    tag_name: string;
    name: string;
    html_url: string;
    prerelease: boolean;
    published_at: string;
    assets: ReleaseAsset[];
}

/**
 * Verify Ed25519 cryptographic signature from Discord
 */
function verifyDiscordSignature(rawBody: string, signature: string, timestamp: string, publicKey: string): boolean {
    try {
        return crypto.verify(
            null,
            Buffer.concat([Buffer.from(timestamp, 'utf-8'), Buffer.from(rawBody, 'utf-8')]),
            {
                key: Buffer.concat([
                    Buffer.from('302a300506032b6570032100', 'hex'),
                    Buffer.from(publicKey, 'hex')
                ]),
                format: 'der',
                type: 'spki'
            },
            Buffer.from(signature, 'hex')
        );
    } catch (error) {
        console.error('Cryptographic signature verification exception:', error);
        return false;
    }
}

/**
 * Validates whether incoming request has a valid Discord signature
 */
function isSignatureValid(req: VercelLikeRequest, rawBody: string, publicKey: string): boolean {
    const signatureHeader = req.headers['x-signature-ed25519'] ?? req.headers['X-Signature-Ed25519'];
    const timestampHeader = req.headers['x-signature-timestamp'] ?? req.headers['X-Signature-Timestamp'];

    const signature = Array.isArray(signatureHeader) ? signatureHeader[0] : signatureHeader;
    const timestamp = Array.isArray(timestampHeader) ? timestampHeader[0] : timestampHeader;

    if (typeof signature !== 'string' || typeof timestamp !== 'string') {
        return false;
    }

    return verifyDiscordSignature(rawBody, signature, timestamp, publicKey);
}

/**
 * Safely parse interaction body
 */
function parseInteractionBody(body: unknown): DiscordInteraction | null {
    if (Buffer.isBuffer(body)) {
        try {
            return JSON.parse(body.toString('utf-8')) as DiscordInteraction;
        } catch (error) {
            console.error('Failed to parse request Buffer body:', error);
            return null;
        }
    }

    if (typeof body === 'object' && body !== null) {
        return body as DiscordInteraction;
    }

    if (typeof body === 'string') {
        try {
            return JSON.parse(body) as DiscordInteraction;
        } catch (error) {
            console.error('Failed to parse request JSON body:', error);
            return null;
        }
    }

    return null;
}

/**
 * Fetch latest release dynamically from GitHub API with strict timeout & error handling
 */
async function fetchLatestRelease(): Promise<GitHubRelease | null> {
    try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 2000); // 2-second timeout for Discord's 3s limit

        const res = await fetch(`https://api.github.com/repos/${GITHUB_REPO}/releases`, {
            headers: {
                'User-Agent': 'LocalTelemetry-Discord-Bot',
                'Accept': 'application/vnd.github.v3+json'
            },
            signal: controller.signal
        });

        clearTimeout(timeoutId);

        if (!res.ok) {
            console.warn(`GitHub API returned non-200 status: ${res.status} ${res.statusText}`);
            return null;
        }

        const releases = (await res.json()) as GitHubRelease[];
        if (Array.isArray(releases) && releases.length > 0) {
            return releases[0];
        }
    } catch (error) {
        console.error('Error fetching releases from GitHub API:', error);
    }
    return null;
}

function getDownloadEmbed(latestRelease: GitHubRelease | null) {
    if (!latestRelease) {
        return {
            content: `🚀 **Download LocalTelemetry:** https://github.com/${GITHUB_REPO}/releases`
        };
    }

    const tag = latestRelease.tag_name;
    const releaseLabel = latestRelease.prerelease ? 'Pre-Release' : 'Release';

    const exeAsset = latestRelease.assets?.find(a => a.name.endsWith('.exe'));
    const zipAsset = latestRelease.assets?.find(a => a.name.endsWith('.zip'));
    const checksumAsset = latestRelease.assets?.find(a => a.name.includes('checksum'));

    const exeUrl = exeAsset?.browser_download_url || `https://github.com/${GITHUB_REPO}/releases/tag/${tag}`;
    const exeName = exeAsset?.name || 'LocalTelemetrySetup.exe';

    const zipUrl = zipAsset?.browser_download_url || `https://github.com/${GITHUB_REPO}/releases/tag/${tag}`;
    const zipName = zipAsset?.name || 'LocalTelemetry-win-x64.zip';

    const checksumUrl = checksumAsset?.browser_download_url || `https://github.com/${GITHUB_REPO}/releases/tag/${tag}`;
    const checksumName = checksumAsset?.name || 'checksums.txt';

    return {
        embeds: [
            {
                title: `🚀 Download LocalTelemetry ${tag} (${releaseLabel})`,
                description: `Direct download links for the latest official build fetched dynamically from GitHub.\n*Target OS: Windows 10 & Windows 11 (64-bit)*`,
                color: 0x52d5d6,
                fields: [
                    {
                        name: '📦 Windows Setup Installer',
                        value: `[${exeName}](${exeUrl})`,
                        inline: true
                    },
                    {
                        name: '🗜️ Portable Zip Archive',
                        value: `[${zipName}](${zipUrl})`,
                        inline: true
                    },
                    {
                        name: '🛡️ Checksums & Hashes',
                        value: `[${checksumName}](${checksumUrl})`,
                        inline: false
                    }
                ],
                footer: {
                    text: 'LocalTelemetry • 100% Offline Hardware Monitoring'
                }
            }
        ]
    };
}

function getFeaturesEmbed() {
    return {
        embeds: [
            {
                title: '⚡ LocalTelemetry Features & Capabilities',
                description: 'A native, lightweight Windows utility delivering real-time hardware telemetry directly onto your taskbar with zero performance impact.',
                color: 0x52d5d6,
                fields: [
                    {
                        name: '🖥️ Native Taskbar Embedding',
                        value: 'Integrates seamlessly into the Windows 10 & 11 taskbar without floating window clutter or intrusive overlays.',
                        inline: false
                    },
                    {
                        name: '⚡ Signed PawnIo Kernel Driver',
                        value: 'Direct, low-overhead CPU Model-Specific Register (MSR) and AMD SMU hardware queries with zero WMI lag.',
                        inline: false
                    },
                    {
                        name: '🛡️ Zero AV False Positives',
                        value: 'Does not drop temporary executables or scripts into `AppData\\Local\\Temp`, guaranteeing a clean security reputation.',
                        inline: false
                    },
                    {
                        name: '🔒 100% Offline & Private',
                        value: 'No third-party telemetry, no cloud sync, and zero outbound network connections. Your hardware data stays on your machine.',
                        inline: false
                    },
                    {
                        name: '🎨 Svelte 5 Customization',
                        value: 'Modular layout system allowing per-sensor threshold colors, layout ordering, and custom taskbar styling.',
                        inline: false
                    }
                ],
                footer: {
                    text: 'Learn more: https://nicconike.github.io/LocalTelemetry/'
                }
            }
        ]
    };
}

function getStatusEmbed(latestRelease: GitHubRelease | null) {
    const tag = latestRelease?.tag_name || 'Latest';
    const releaseLabel = latestRelease?.prerelease ? 'Latest Pre-Release' : 'Latest Release';

    return {
        embeds: [
            {
                title: '📊 LocalTelemetry Project Status',
                description: 'Official software runtime, driver, and architecture specifications.',
                color: 0x52d5d6,
                fields: [
                    { name: releaseLabel, value: `\`${tag}\``, inline: true },
                    { name: 'Framework', value: '.NET 10.0 + Svelte 5', inline: true },
                    { name: 'Platform', value: 'Windows 10 / 11 (x64)', inline: true },
                    { name: 'Kernel Driver', value: 'Signed PawnIo Driver (MSR & SMU)', inline: true },
                    { name: 'Privacy', value: '100% Local (Zero Network Calls)', inline: true },
                    { name: 'False Positives', value: 'Zero Temp File Drops', inline: true }
                ],
                footer: {
                    text: 'Docs: https://nicconike.github.io/LocalTelemetry/'
                }
            }
        ]
    };
}

function getVerifyEmbed() {
    return {
        embeds: [
            {
                title: '🛡️ SLSA Level 3 Attestation Verification',
                description: 'Verify binary authenticity and supply chain provenance using the official GitHub CLI:',
                color: 0x2ecc71,
                fields: [
                    {
                        name: 'Verify Installer (PowerShell)',
                        value: `\`\`\`powershell\ngh attestation verify LocalTelemetrySetup.exe --repo ${GITHUB_REPO}\n\`\`\``
                    },
                    {
                        name: 'Verify Portable Zip (PowerShell)',
                        value: `\`\`\`powershell\ngh attestation verify LocalTelemetry-win-x64.zip --repo ${GITHUB_REPO}\n\`\`\``
                    }
                ],
                footer: {
                    text: `Guide: https://github.com/${GITHUB_REPO}#verifying-your-download`
                }
            }
        ]
    };
}

function getDocsEmbed() {
    return {
        embeds: [
            {
                title: '📖 Official Documentation & Guides',
                description: 'Explore comprehensive guides on taskbar layout personalization, sensor monitoring, and driver architecture.',
                color: 0x3b82f6,
                fields: [
                    { name: 'Documentation Website', value: '[nicconike.github.io/LocalTelemetry](https://nicconike.github.io/LocalTelemetry/)' },
                    { name: 'GitHub Repository', value: `[github.com/${GITHUB_REPO}](https://github.com/${GITHUB_REPO})` },
                    { name: 'Contributing Guide', value: `[CONTRIBUTING.md](https://github.com/${GITHUB_REPO}/blob/master/CONTRIBUTING.md)` }
                ]
            }
        ]
    };
}

function getRequirementsEmbed() {
    return {
        embeds: [
            {
                title: '⚙️ System Requirements & Supported Hardware',
                description: 'Hardware and OS compatibility matrix supported by LocalTelemetry.',
                color: 0x9b59b6,
                fields: [
                    { name: 'Operating System', value: '• Windows 10 (64-bit, v2004+)\n• Windows 11 (64-bit, v22H2+)', inline: true },
                    { name: 'Architecture', value: '• x86-64 / AMD64 (Intel / AMD)\n• Administrator (UAC) for PawnIo', inline: true },
                    { name: 'CPU Monitoring', value: '• Intel Core / Xeon (PawnIo MSR)\n• AMD Ryzen / Threadripper (PawnIo SMU)', inline: false },
                    { name: 'GPU Monitoring', value: '• NVIDIA (NVAPI)\n• AMD Radeon (ADLX / ADL)\n• Intel Arc / Xe (IGCL / ControlLib)\n• WDDM Generic Fallback', inline: false },
                    { name: 'RAM & Disk Space', value: '• Min 2 GB RAM (4 GB recommended)\n• 200 MB Free Disk Space', inline: false }
                ],
                footer: {
                    text: `Requirements: https://github.com/${GITHUB_REPO}#system-requirements`
                }
            }
        ]
    };
}

function getHelpEmbed() {
    return {
        embeds: [
            {
                title: '🤖 LocalTelemetry Bot Commands',
                description: 'Available slash commands to explore releases, features, and documentation:',
                color: 0x52d5d6,
                fields: [
                    { name: '`/download`', value: 'Get direct download links for the latest installer and zip release.', inline: true },
                    { name: '`/features`', value: 'Overview of core features, taskbar embedding, and offline privacy.', inline: true },
                    { name: '`/status`', value: 'View latest release tag, runtime framework, and driver state.', inline: true },
                    { name: '`/verify`', value: 'Instructions to verify binary authenticity via SLSA provenance.', inline: true },
                    { name: '`/requirements`', value: 'Supported CPU/GPU hardware and Windows OS compatibility matrix.', inline: true },
                    { name: '`/docs`', value: 'Links to official documentation guides and GitHub repository.', inline: true }
                ],
                footer: {
                    text: 'LocalTelemetry Community Server • Type / for commands'
                }
            }
        ]
    };
}

/**
 * Dispatches command name to corresponding embed builder
 */
async function dispatchCommand(commandName?: string) {
    try {
        if (commandName === 'download') {
            const release = await fetchLatestRelease();
            return getDownloadEmbed(release);
        }

        if (commandName === 'features') {
            return getFeaturesEmbed();
        }

        if (commandName === 'status') {
            const release = await fetchLatestRelease();
            return getStatusEmbed(release);
        }

        if (commandName === 'verify') {
            return getVerifyEmbed();
        }

        if (commandName === 'docs') {
            return getDocsEmbed();
        }

        if (commandName === 'requirements' || commandName === 'specs') {
            return getRequirementsEmbed();
        }

        if (commandName === 'help') {
            return getHelpEmbed();
        }

        return { content: 'LocalTelemetry Bot is online. Type `/help` for available commands.' };
    } catch (error) {
        console.error(`Error dispatching command ${commandName}:`, error);
        return { content: '⚠️ An error occurred while processing this command. Please try again later.' };
    }
}

export const config = {
    api: {
        bodyParser: false,
    },
};

async function getRawBody(req: unknown): Promise<string> {
    if (typeof req === 'object' && req !== null && 'body' in req) {
        const body = (req as { body: unknown }).body;
        if (typeof body === 'string') return body;
        if (Buffer.isBuffer(body)) return body.toString('utf-8');
    }

    if (typeof req === 'object' && req !== null && Symbol.asyncIterator in req) {
        const chunks: Buffer[] = [];
        for await (const chunk of req as AsyncIterable<Buffer | string>) {
            chunks.push(typeof chunk === 'string' ? Buffer.from(chunk) : chunk);
        }
        return Buffer.concat(chunks).toString('utf-8');
    }

    return '';
}

export default async function handler(req: VercelLikeRequest, res: VercelLikeResponse): Promise<void> {
    try {
        if (req.method !== 'POST') {
            res.status(405).json({ error: 'Method Not Allowed' });
            return;
        }

        const publicKey = process.env.DISCORD_PUBLIC_KEY;
        if (!publicKey) {
            console.error('Server error: DISCORD_PUBLIC_KEY environment variable is not configured.');
            res.status(500).json({ error: 'Server misconfiguration: Missing DISCORD_PUBLIC_KEY' });
            return;
        }

        const rawBody = await getRawBody(req);
        if (!rawBody || !isSignatureValid(req, rawBody, publicKey)) {
            res.status(401).send('Invalid request signature');
            return;
        }

        const interaction = parseInteractionBody(rawBody);
        if (!interaction) {
            res.status(400).json({ error: 'Invalid interaction payload' });
            return;
        }

        // 1. Discord PING verification (Endpoint handshake)
        if (interaction.type === 1) {
            res.status(200).json({ type: 1 });
            return;
        }

        // 2. Application Slash Commands (Type 2)
        const responseData = await dispatchCommand(interaction.data?.name);
        res.status(200).json({
            type: 4,
            data: responseData
        });
    } catch (fatalError) {
        console.error('Unhandled fatal error in Discord interaction handler:', fatalError);
        res.status(500).json({ error: 'Internal Server Error' });
    }
}


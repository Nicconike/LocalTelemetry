import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

// Relax semantic-release requirement of having at least 1 non-prerelease branch
// when master itself is configured as a prerelease (rc) channel.
const branchesDefsPath = resolve(dirname(fileURLToPath(import.meta.url)), 'node_modules/semantic-release/lib/definitions/branches.js');
const defs = await import(branchesDefsPath);
defs.release.branchesValidator = (branches: unknown[]) => branches.length <= 3;

// Execute standard semantic-release CLI
const cliPath = resolve(dirname(fileURLToPath(import.meta.url)), 'node_modules/semantic-release/cli.js');
const cli = await import(cliPath);
await cli.default();

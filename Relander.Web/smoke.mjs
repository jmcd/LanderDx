// Headless smoke test: boot the published Relander wasm bundle under Node (the
// engine needs no DOM) and verify it renders frames in both view modes.
//
//   dotnet publish -c Release && node smoke.mjs
//
// Expected: non-zero pixel counts in the tens of thousands, and the same
// numbers on every run (the engine is deterministic for a fixed seed).
import { dotnet } from './bin/Release/net10.0/browser-wasm/AppBundle/_framework/dotnet.js';

const runtime = await dotnet.create();
const { getAssemblyExports, getConfig } = runtime;
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

exports.Program.Start(false);
console.log('width:', exports.Program.GetWidth());
const palette = exports.Program.GetPalette();
console.log('palette bytes:', palette.length, 'first rgba:', Array.from(palette.slice(0, 8)));

// Run 30 game ticks with no input
for (let i = 0; i < 30; i++) exports.Program.Update(0, 0);

const screen = exports.Program.GetScreen();
let nonZero = 0;
for (const b of screen) if (b !== 0) nonZero++;
console.log('screen bytes:', screen.length, 'non-zero (indices):', nonZero);

// Widescreen path too
exports.Program.Start(true);
console.log('widescreen width:', exports.Program.GetWidth());
for (let i = 0; i < 5; i++) exports.Program.Update(0, 0);
const wide = exports.Program.GetScreen();
let wideNonZero = 0;
for (const b of wide) if (b !== 0) wideNonZero++;
console.log('widescreen non-zero:', wideNonZero, 'of', wide.length);

// Mirrors the browser flow: runMain keeps the runtime alive for the loop.
await runtime.runMain();
console.log('SMOKE OK');

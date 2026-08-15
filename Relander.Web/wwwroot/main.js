import { dotnet } from './_framework/dotnet.js';

const { getAssemblyExports, getConfig } = await dotnet
    .withDiagnosticTracing(false)
    .create();
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

// Widescreen via ?widescreen=1, mirroring the desktop --widescreen flag.
const params = new URLSearchParams(location.search);
const widescreen = params.get('widescreen') === '1';

exports.Program.Start(widescreen);
const width = exports.Program.GetWidth();
const height = 256;
const scale = widescreen ? 3 : 4;

const canvas = document.getElementById('game');
canvas.width = width;
canvas.height = height;
canvas.style.width = `${width * scale}px`;
canvas.style.height = `${height * scale}px`;
canvas.style.imageRendering = 'pixelated';
const ctx = canvas.getContext('2d');
const image = ctx.createImageData(width, height);
const palette = exports.Program.GetPalette();  // interleaved r,g,b,a bytes

// Key state bitmasks (see Program.Update): held = level-triggered,
// pressed = edge-triggered and consumed once per game tick.
const KEYMAP = {
    KeyA: 1, KeyD: 2, KeyW: 4, KeyS: 8,
    KeyN: 16, KeyM: 32, KeyH: 64,
    Tab: 128, KeyR: 128,
    Escape: 256,
};
let held = 0;
let pressed = 0;

window.addEventListener('keydown', (e) => {
    const bit = KEYMAP[e.code];
    if (bit === undefined) return;
    e.preventDefault();
    held |= bit;
    if (!e.repeat) pressed |= bit;
});
window.addEventListener('keyup', (e) => {
    const bit = KEYMAP[e.code];
    if (bit !== undefined) held &= ~bit;
});

// Fixed-step accumulator, mirroring the desktop Program.cs: the game logic
// runs at the authentic ~12.5 Hz regardless of the display refresh rate.
const TICK = 1 / 12.5;
let acc = 0;
let last = performance.now();

function frame(now) {
    acc += (now - last) / 1000;
    last = now;
    if (acc > TICK) acc = TICK;  // stall = drop time, never fast-forward

    let stale = false;
    while (acc >= TICK) {
        const running = exports.Program.Update(held, pressed);
        pressed = 0;
        if (!running) exports.Program.Start(widescreen);  // Escape = new game
        acc -= TICK;
        stale = true;
    }

    if (stale) {
        const screen = exports.Program.GetScreen();
        const rgba = image.data;
        for (let i = 0; i < screen.length; i++) {
            const p = screen[i] * 4;
            const o = i * 4;
            rgba[o] = palette[p];
            rgba[o + 1] = palette[p + 1];
            rgba[o + 2] = palette[p + 2];
            rgba[o + 3] = palette[p + 3];
        }
        ctx.putImageData(image, 0, 0);
    }

    requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

// Enter managed code (Program.Main is a no-op — the JS side drives the loop,
// but this matches the canonical boot sequence).
await dotnet.run();

import { dotnet } from './_framework/dotnet.js';

const runtime = await dotnet
    .withDiagnosticTracing(false)
    .create();
const { getAssemblyExports, getConfig } = runtime;
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

// Widescreen is the default (mirroring the desktop); ?original=1 selects the
// original 320x256 view, like the desktop's --fullscreen flag.
const params = new URLSearchParams(location.search);
const original = params.get('original') === '1';

exports.Program.Start(!original);
const width = exports.Program.GetWidth();
const height = 256;
const scale = original ? 4 : 3;

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
    // P toggles the coordinate/landing display directly (it is not part of
    // the per-tick key bitmask).
    if (e.code === 'KeyP' && !e.repeat) {
        e.preventDefault();
        exports.Program.ToggleCoords();
        return;
    }
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
        if (!running) exports.Program.Start(!original);  // Escape = new game
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

// Enter managed code (Program.Main is a no-op — the JS side drives the loop).
// runMain() runs Main WITHOUT exiting the runtime, so the rAF loop can keep
// calling the exported Update() afterwards (dotnet.run() would exit and make
// the first frame throw "runtime already exited").
await runtime.runMain();

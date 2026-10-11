// Run identical current-build coverage against explicit Skia vector / depth-tested raster fallback.
// The default browser-tests.mjs invocation still requires real WebGL submissions.
process.env.RENDER_MODE='Skia';
await import('./browser-tests.mjs');

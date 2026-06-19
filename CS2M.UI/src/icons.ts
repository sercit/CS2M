// Cohtml on AMD driver 32.0.21033.2001 hangs when loading SVG files through
// the game's asset loader (e.g. "Media/Game/Icons/Communications.svg"). Inline
// data URLs bypass the loader entirely and render fine. Keep this as the single
// source of truth so every icon path in the mod uses the safe variant.
export const MP_ICON = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDQwIDQwIj48Y2lyY2xlIGN4PSIyMCIgY3k9IjIwIiByPSIxNSIgZmlsbD0iIzVjYjZmZiIvPjwvc3ZnPg==";
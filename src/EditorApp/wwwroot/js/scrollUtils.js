"use strict";
// scrollUtils.ts — Pure utility functions for horizontal scroll logic.
// Compiled with module: "none" — no import/export. Exposed on window for runtime use.
/**
 * Clamp a scroll column value to [0, maxLineLength].
 * Scrolling stops when last char of longest line goes just past the left edge.
 */
function clampScrollColumn(col, maxLineLength, viewportColumns) {
    return Math.max(0, Math.min(col, Math.max(0, maxLineLength)));
}
// Expose on window for use by other scripts (ContentArea, etc.)
window.clampScrollColumn = clampScrollColumn;

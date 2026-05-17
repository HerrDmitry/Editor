"use strict";
// determineStickyLine.ts — Pure function for sticky line number determination.
// Compiled with module: "none" — no import/export. Exposed on window for runtime use and testing.
/**
 * Determine which line index (if any) should appear sticky given current scroll state.
 * Returns -1 if no line should be sticky.
 */
function determineStickyLine(wrapMode, lineHeights, // height of each line container in px
scrollTop, // current viewport scroll offset
lineHeight // single-row height (20px)
) {
    if (!wrapMode)
        return -1;
    let accumulatedTop = 0;
    let stickyIndex = -1;
    for (let i = 0; i < lineHeights.length; i++) {
        const containerTop = accumulatedTop;
        const containerBottom = accumulatedTop + lineHeights[i];
        // Line is a wrapped line (height > single row) AND partially scrolled above viewport
        if (lineHeights[i] > lineHeight && containerTop < scrollTop && containerBottom > scrollTop) {
            stickyIndex = i;
        }
        accumulatedTop += lineHeights[i];
        // Once we pass the viewport top, no further lines can be sticky
        if (containerTop >= scrollTop)
            break;
    }
    return stickyIndex;
}
// Expose on window for runtime use and testing
window.determineStickyLine = determineStickyLine;

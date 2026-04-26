# Custom Scrollbar

## Overview
Replace the native vertical scrollbar with a custom scrollbar that operates in line-number space rather than pixel space. The native scrollbar is hidden but still handles wheel/trackpad input for smooth scrolling. The custom scrollbar reflects the current line position and allows direct navigation by dragging.

## Architecture

```
┌──────────────────────────────────────────────────┐
│ Content Area                                      │
│ ┌────────┬──────────────────────────┬───────────┐│
│ │ Line   │ Content Column           │ Custom    ││
│ │ Numbers│ (native scroll hidden)   │ Scrollbar ││
│ │ Column │                          │           ││
│ │        │ ┌──────────────────────┐ │ ┌───────┐ ││
│ │   35   │ │ line 35 content...   │ │ │ track │ ││
│ │   36   │ │ line 36 content...   │ │ │       │ ││
│ │   37   │ │ line 37 content...   │ │ │ thumb │ ││
│ │   ...  │ │ ...                  │ │ │       │ ││
│ │        │ └──────────────────────┘ │ │       │ ││
│ │        │                          │ └───────┘ ││
│ └────────┴──────────────────────────┴───────────┘│
└──────────────────────────────────────────────────┘
```

Three columns:
1. **Line numbers** (60px, synced vertically with content)
2. **Content** (flex, native scroll hidden via CSS, handles wheel/trackpad)
3. **Custom scrollbar** (14px, positioned on the right)

## Tasks

- [x] 1. Create CustomScrollbar component
  - [x] 1.1 Create src/EditorApp/src/CustomScrollbar.tsx
    - Props: totalLines, visibleLineCount, currentTopLine, onScrollToLine
    - Render a track div (full height of the component) and a thumb div (sized proportionally)
    - Thumb position: `(currentTopLine / totalLines) * trackHeight`
    - Thumb size: `Math.max(20, (visibleLineCount / totalLines) * trackHeight)` (minimum 20px)
  - [x] 1.2 Implement thumb dragging
    - On mousedown on thumb: start tracking mouse movement
    - On mousemove: calculate new line from mouse position relative to track
    - On mouseup: stop tracking
    - Call `onScrollToLine(targetLine)` during drag
  - [x] 1.3 Implement track click (page up/down)
    - Click above thumb: `onScrollToLine(currentTopLine - visibleLineCount)`
    - Click below thumb: `onScrollToLine(currentTopLine + visibleLineCount)`
  - [x] 1.4 Add to wwwroot/index.html script loading order
    - Add CustomScrollbar.js before App.js

- [x] 2. Hide native scrollbar on content column
  - Update CSS to hide the native vertical scrollbar on the content column
  - Use `scrollbar-width: none` (Firefox) and `::-webkit-scrollbar { display: none }` (Chrome/WebKit)
  - Keep `overflow-y: scroll` so wheel/trackpad still works

- [x] 3. Integrate into ContentArea
  - [x] 3.1 Add CustomScrollbar to the layout
    - Add as a third column in the content area (after line numbers and content)
    - Pass totalLines, visibleLineCount, currentTopLine, onScrollToLine
  - [x] 3.2 Calculate currentTopLine from scroll position
    - On content scroll: `currentTopLine = Math.floor(scrollTop / LINE_HEIGHT)`
    - Pass this to CustomScrollbar for thumb positioning
  - [x] 3.3 Handle onScrollToLine from CustomScrollbar
    - When the user drags the scrollbar or clicks the track:
    - Set `containerRef.current.scrollTop = targetLine * LINE_HEIGHT`
    - This triggers the normal scroll handler which requests lines from the backend
  - [x] 3.4 Calculate visibleLineCount
    - `visibleLineCount = Math.ceil(containerRef.current.clientHeight / LINE_HEIGHT)`

- [x] 4. Style the custom scrollbar
  - Track: dark background (#2a2a2a), full height, 14px wide
  - Thumb: lighter color (#555), rounded corners, hover highlight (#777)
  - Active/dragging: brighter (#888)
  - Smooth transitions for thumb position

- [x] 5. Verify
  - Build and run
  - Native scrollbar should be hidden
  - Custom scrollbar thumb should reflect current position
  - Mouse wheel scrolling should work smoothly and update the custom scrollbar
  - Dragging the custom scrollbar thumb should navigate to the correct line
  - Clicking the track should page up/down
  - Test with small and large files
  - Line numbers should stay in sync

## Notes
- The custom scrollbar is purely visual — it doesn't handle the actual content scrolling. The native (hidden) scroll handles wheel/trackpad, and the custom scrollbar just reflects the position.
- When the user drags the custom scrollbar, it sets `scrollTop` on the content container, which triggers the normal scroll flow.
- This design makes it easy to add line wrapping later — the custom scrollbar can be updated to account for variable line heights without changing the scroll mechanism.

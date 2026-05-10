# Design Document: Horizontal Scrollbar

## Overview

Replace the existing HTML `<input type="range">` horizontal scrollbar with a proper `CustomScrollbar` component oriented horizontally. The new scrollbar appears in no-wrap mode whenever `maxLineLength > viewportColumns`, provides thumb-drag and Shift+wheel scrolling, integrates with the existing large-line chunk loading system, and adapts to window resizes.

The design reuses the existing `CustomScrollbar` component (adding horizontal orientation support) and extends `ContentArea` state management to track `viewportColumns` dynamically rather than using the hardcoded `H_VIEWPORT_CHARS` constant.

## Architecture

```mermaid
graph TD
    subgraph Frontend
        CA[ContentArea]
        HCS[CustomScrollbar - horizontal]
        VCS[CustomScrollbar - vertical]
        VR[ViewportRenderer]
    end

    subgraph Backend
        VS[ViewportService]
        FS[FileService]
    end

    CA -->|props: range, position, viewportSize| HCS
    CA -->|props: range, position, viewportSize| VCS
    CA -->|scrollColumn, viewportColumns| VR
    CA -->|getViewport(startCol, colCount)| VS
    VS -->|readLineChunk| FS

    HCS -->|onPositionChange| CA
    CA -->|Shift+wheel| CA
    CA -->|resize observer| CA
```

### Data Flow

1. **Resize** → `ResizeObserver` on content area → compute `viewportColumns = floor(pixelWidth / charCellWidth)` → update state
2. **Visibility** → `!wrapMode && maxLineLength > viewportColumns` → show/hide scrollbar
3. **Thumb drag** → `CustomScrollbar.onPositionChange(newScrollCol)` → `setScrollColumn(clamp(newScrollCol))` → re-render content at new offset
4. **Shift+wheel** → `deltaX` or `shiftKey+deltaY` → adjust `scrollColumn ± 3*ticks` → clamp → update scrollbar + content
5. **Vertical scroll** → viewport request includes current `scrollColumn` as `startColumn` → backend returns content at correct horizontal offset
6. **Large lines** → chunk requests issued only for large lines visible at current `scrollColumn`

## Components and Interfaces

### CustomScrollbar (modified)

Add an `orientation` prop to support horizontal layout. The component already computes thumb size and position from abstract `range`/`position`/`viewportSize` — only CSS/layout changes needed.

```typescript
interface CustomScrollbarProps {
  range: number;           // Total scrollable extent
  position: number;        // Current offset (0 to range)
  viewportSize: number;    // Viewport size in same units (for thumb sizing)
  orientation?: 'vertical' | 'horizontal';  // NEW — default 'vertical'
  onPositionChange?: (position: number) => void;
}
```

**Changes:**
- When `orientation === 'horizontal'`: track renders horizontally (height: 14px, width: 100%), thumb uses `left`/`width` instead of `top`/`height`
- Mouse tracking uses `clientX` and horizontal delta instead of `clientY`/vertical delta
- CSS classes: `.custom-scrollbar--horizontal`, `.custom-scrollbar--horizontal .custom-scrollbar__track`, `.custom-scrollbar--horizontal .custom-scrollbar__thumb`

### ContentArea (modified)

**New state:**
- `viewportColumns: number` — computed from content area pixel width via ResizeObserver (subtracts 72px gutter)
- `scrollColumn: number` — replaces `hScrollCol`, clamped to `[0, maxLineLength]`
- `charCellWidth: number` — measured once from a hidden monospace character probe element

**New props (from App.tsx):**
- `lineLengths: number[] | null` — per-line character lengths from backend (always sent)

**New refs:**
- `contentMeasureRef` — ref to content column div for ResizeObserver
- `charProbeRef` — ref to hidden `<span>` for measuring monospace char width

**Removed:**
- `H_VIEWPORT_CHARS` constant (replaced by dynamic `viewportColumns`)
- `<input type="range">` horizontal scrollbar element
- Internal `lineLengths` state (now a prop from App.tsx)

**New behavior:**
- `ResizeObserver` on viewport div → recalculate `viewportColumns = floor((pixelWidth - 72) / charCellWidth)`
- Shift+wheel handler → adjust `scrollColumn` by ±3 per tick (only pure horizontal or shift+wheel)
- Clamp `scrollColumn` to `[0, maxLineLength]` on every state update
- Normal lines render `line.slice(scrollColumn, scrollColumn + viewportColumns)` in no-wrap mode
- Large lines use chunk cache with debounced requests (150ms batch timer)
- Render `CustomScrollbar` with `orientation="horizontal"` below content

### App.tsx (modified)

- Exposes `window.interopService` for ContentArea's chunk handlers
- Captures `lineLengths` from `LinesResponse` and passes as prop to ContentArea
- `onRequestLines`/`onJumpToLine` use 2-param signatures (no startColumn/columnCount — backend ignores them)

### ViewportService (backend — no changes needed)

Already supports `startColumn` and `columnCount` parameters for chunk requests.

### FileService (backend — modified)

- `ReadLinesAsync` always sends `lineLengths` array (not null for normal files)
- Provides accurate per-line character counts for scrollbar range calculation

### InteropService (minor change)

- `sendRequestLines` has optional `startColumn`/`columnCount` params (defaults to 0, backend ignores)
- `sendRequestLineChunk` used for large-line horizontal virtualization

## Data Models

### Frontend State (ContentArea)

```typescript
// Replaces H_VIEWPORT_CHARS constant
const [viewportColumns, setViewportColumns] = useState<number>(200); // initial fallback

// Replaces hScrollCol
const [scrollColumn, setScrollColumn] = useState<number>(0);

// Measured char cell width (pixels per monospace character)
const [charCellWidth, setCharCellWidth] = useState<number>(7.2); // typical default
```

### Scrollbar Props Computation

```typescript
// Horizontal scrollbar range: allows scrolling until last char is at right edge
const hScrollbarRange = maxLineLength > viewportColumns
  ? maxLineLength - viewportColumns
  : viewportColumns;
const hScrollbarPosition = scrollColumn;
const hScrollbarViewportSize = viewportColumns;
```

### Clamping Logic

```typescript
// Clamps to maxLineLength (not maxLineLength - viewportColumns)
// The scrollbar range limits the thumb position; clamp is a safety net
function clampScrollColumn(col: number, maxLineLength: number, viewportColumns: number): number {
  return Math.max(0, Math.min(col, Math.max(0, maxLineLength)));
}
```

### maxLineLength Computation

```typescript
// Primary: lineLengths from backend (exact char counts)
// Secondary: line.length from buffer
// Fallback: fileMeta.maxLineLength (byte-based approximate)
const maxLineLength = useMemo(() => {
  if (lineLengths?.length) return Math.max(...lineLengths);
  if (lines?.length) return Math.max(...lines.map(l => l.length));
  return fileMeta?.maxLineLength ?? 0;
}, [fileMeta, lineLengths, lines]);
```

### Viewport Request Parameters

```typescript
// When requesting viewport content:
{
  startLine: number,
  lineCount: number,
  startColumn: scrollColumn,      // was 0
  columnCount: viewportColumns,   // was large constant
  wrapMode: boolean,
  viewportColumns: number
}
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Scrollbar visibility determination

*For any* combination of `(wrapMode, maxLineLength, viewportColumns)`, the horizontal scrollbar is visible if and only if `wrapMode === false` AND `maxLineLength > viewportColumns`.

**Validates: Requirements 1.1, 1.2, 1.3, 1.4**

### Property 2: ScrollColumn clamping invariant

*For any* `scrollColumn` value (including negative, zero, and values exceeding the maximum), the effective scroll column after clamping equals `Math.max(0, Math.min(scrollColumn, Math.max(0, maxLineLength - viewportColumns)))`.

**Validates: Requirements 3.5, 4.3, 5.3, 5.4**

### Property 3: Scrollbar prop computation

*For any* valid `(maxLineLength, viewportColumns, scrollColumn)` where `maxLineLength > viewportColumns`, the horizontal scrollbar receives `range = maxLineLength - viewportColumns`, `position = scrollColumn`, and `viewportSize = viewportColumns`.

**Validates: Requirements 2.1, 3.2**

### Property 4: Wheel scroll behavior

*For any* wheel event with `wheelTicks` ticks: if `wrapMode === true`, `scrollColumn` remains unchanged; if `wrapMode === false`, `scrollColumn` changes by `3 × wheelTicks` (positive = right, negative = left), then clamped to `[0, maxLineLength - viewportColumns]`.

**Validates: Requirements 4.1, 4.3, 4.4**

### Property 5: Viewport column calculation

*For any* content area pixel width `W` and character cell pixel width `C` where `C > 0`, `viewportColumns = floor(W / C)`.

**Validates: Requirements 5.1**

### Property 6: Vertical scroll preserves horizontal position

*For any* vertical scroll action, the `scrollColumn` value before and after the scroll are identical, and any viewport request issued includes `startColumn = scrollColumn`.

**Validates: Requirements 6.1, 6.2**

### Property 7: Selective chunk requesting for large lines

*For any* `scrollColumn` change with a set of visible lines, chunk requests are issued if and only if a line's character length exceeds `LARGE_LINE_THRESHOLD` AND the line's length exceeds `scrollColumn`. Normal lines (≤ 65,536 chars) never trigger chunk requests.

**Validates: Requirements 7.1, 7.2, 7.5**

### Property 8: Stale chunk response caching

*For any* chunk response arriving for a `(lineNumber, startColumn)` where the current `scrollColumn` has moved such that the chunk's range no longer intersects the visible viewport, the chunk is stored in the cache but the viewport renders content at the current `scrollColumn` position (not the stale position).

**Validates: Requirements 7.4**

## Error Handling

| Scenario | Handling |
|----------|----------|
| `charCellWidth` measurement returns 0 | Fall back to default 7.2px; log warning |
| `maxLineLength` is 0 or undefined | Hide scrollbar; scrollColumn stays 0 |
| ResizeObserver not supported | Fall back to `window.resize` event with debounce |
| Chunk request timeout for large line | Show placeholder spaces; retry on next scroll stop |
| `viewportColumns` becomes 0 (collapsed window) | Clamp to minimum 1; hide scrollbar |
| Backend returns `maxLineLength` smaller than `scrollColumn` | Clamp scrollColumn to new max on next viewport response |

## Testing Strategy

### Property-Based Tests (fast-check)

PBT is appropriate here — the core logic involves pure computations (visibility, clamping, prop calculation, viewport column math) with large input spaces.

**Library:** fast-check (already in `tests/frontend/`)
**Configuration:** Minimum 100 iterations per property test
**Tag format:** `Feature: horizontal-scrollbar, Property {N}: {title}`

Each correctness property maps to one property-based test:
1. Visibility determination — generate random (wrapMode, maxLineLength, viewportColumns)
2. Clamping invariant — generate random scrollColumn with random (maxLineLength, viewportColumns)
3. Prop computation — generate valid triples, verify formula
4. Wheel behavior — generate (wrapMode, currentCol, wheelTicks, maxLineLength, viewportColumns)
5. Viewport column calc — generate random (pixelWidth, charCellWidth)
6. Vertical scroll invariant — generate scroll events, verify column preserved
7. Selective chunk requesting — generate line arrays with mixed lengths
8. Stale chunk caching — generate chunk responses with moved scrollColumn

### Unit Tests (vitest)

- CustomScrollbar horizontal orientation renders correct CSS classes
- Thumb width matches proportional formula for specific values
- Minimum thumb width enforced at 20px
- ResizeObserver callback updates viewportColumns
- Shift+wheel event handler fires with correct delta
- Scrollbar hidden in wrap mode (specific example)
- Char cell measurement probe element works

### Integration Tests

- Thumb drag updates content viewport (simulated mouse events)
- Vertical scroll maintains horizontal offset in rendered content
- Large-line chunk request issued on horizontal scroll
- Window resize triggers scrollbar visibility change
- File open with long lines shows scrollbar immediately

# Requirements Document

## Introduction

Add a horizontal scrollbar to the editor for navigating wide content in no-wrap mode. The current implementation uses a basic HTML range input that only appears for lines exceeding 65,536 characters. This feature replaces it with a proper custom scrollbar component (matching the vertical scrollbar style) that appears whenever content exceeds the visible viewport width, enabling smooth horizontal navigation for all line lengths.

## Glossary

- **Horizontal_Scrollbar**: A custom scrollbar UI component rendered below the content area, oriented horizontally, allowing the user to scroll content left/right in no-wrap mode.
- **Content_Area**: The main editor viewport that displays file content with virtual scrolling.
- **Viewport_Columns**: The number of character columns visible in the content area at the current width.
- **Max_Line_Length**: The maximum character length across all lines in the currently open file, as reported by the backend.
- **Scroll_Column**: The zero-based character column offset representing the leftmost visible column.
- **No_Wrap_Mode**: Display mode where lines are not wrapped and may extend beyond the visible viewport width.
- **CustomScrollbar**: The existing reusable scrollbar component that accepts abstract range/position/viewportSize props and renders a draggable thumb on a track.

## Requirements

### Requirement 1: Horizontal Scrollbar Visibility

**User Story:** As a user, I want a horizontal scrollbar to appear when lines are wider than the viewport, so that I can see there is more content to the right.

#### Acceptance Criteria

1. WHILE No_Wrap_Mode is active AND Max_Line_Length exceeds Viewport_Columns, THE Content_Area SHALL display the Horizontal_Scrollbar below the content viewport.
2. WHILE No_Wrap_Mode is active AND Max_Line_Length is less than or equal to Viewport_Columns, THE Content_Area SHALL hide the Horizontal_Scrollbar.
3. WHILE Wrap_Mode is active, THE Content_Area SHALL hide the Horizontal_Scrollbar regardless of line lengths.
4. WHEN a file is opened in No_Wrap_Mode, THE Content_Area SHALL display the Horizontal_Scrollbar if the reported Max_Line_Length exceeds the current Viewport_Columns, or hide it otherwise.
5. WHEN the Viewport_Columns value changes due to a window resize, THE Content_Area SHALL re-evaluate Horizontal_Scrollbar visibility by comparing the current Max_Line_Length against the new Viewport_Columns value.

### Requirement 2: Horizontal Scrollbar Rendering

**User Story:** As a user, I want the horizontal scrollbar to look and behave like the vertical scrollbar, so that the UI is consistent.

#### Acceptance Criteria

1. THE Horizontal_Scrollbar SHALL reuse the CustomScrollbar component with horizontal orientation, passing range equal to (Max_Line_Length - Viewport_Columns) when Max_Line_Length exceeds Viewport_Columns (or Viewport_Columns otherwise), position equal to Scroll_Column, and viewportSize equal to Viewport_Columns.
2. THE Horizontal_Scrollbar SHALL render a thumb whose width is computed as (Viewport_Columns / Max_Line_Length) multiplied by the track width, matching the same proportional algorithm used by the vertical scrollbar for thumb sizing.
3. THE Horizontal_Scrollbar SHALL position the thumb at a fraction of (Scroll_Column / (Max_Line_Length - Viewport_Columns)) along the available track width (track width minus thumb width).
4. THE Horizontal_Scrollbar SHALL render with a minimum thumb width of 20 pixels regardless of the Viewport_Columns to Max_Line_Length ratio.

### Requirement 3: Horizontal Scroll via Thumb Drag

**User Story:** As a user, I want to drag the horizontal scrollbar thumb to scroll content left and right, so that I can navigate to any column in the file.

#### Acceptance Criteria

1. WHEN the user presses the mouse button on the Horizontal_Scrollbar thumb, THE Horizontal_Scrollbar SHALL begin tracking mouse movement along the track axis.
2. WHILE the Horizontal_Scrollbar is tracking mouse movement, WHEN the mouse position changes, THE Content_Area SHALL update Scroll_Column using the formula: Scroll_Column = round(thumb_fraction × (Max_Line_Length - Viewport_Columns)), where thumb_fraction is the thumb center position relative to the track length in the range [0, 1].
3. WHEN the Scroll_Column changes due to thumb drag, THE Content_Area SHALL re-render visible content starting at the new Scroll_Column.
4. WHEN the user releases the mouse button during a thumb drag, THE Horizontal_Scrollbar SHALL stop tracking mouse movement and finalize the current Scroll_Column.
5. THE Horizontal_Scrollbar SHALL clamp Scroll_Column to the range [0, Max_Line_Length].
6. IF Max_Line_Length is less than or equal to Viewport_Columns, THEN THE Horizontal_Scrollbar SHALL remain at position 0 and SHALL NOT respond to drag input.

### Requirement 4: Horizontal Scroll via Keyboard and Wheel

**User Story:** As a user, I want to scroll horizontally using Shift+scroll wheel, so that I can navigate wide content without using the scrollbar thumb.

#### Acceptance Criteria

1. WHILE in No_Wrap_Mode, WHEN the user performs a Shift+wheel scroll, THE Content_Area SHALL adjust Scroll_Column by 3 columns per wheel tick in the wheel direction (wheel-up or wheel-left scrolls left, wheel-down or wheel-right scrolls right).
2. WHEN Scroll_Column changes via wheel input, THE Horizontal_Scrollbar SHALL update its thumb position to reflect the new Scroll_Column proportionally within the scrollbar range.
3. THE Content_Area SHALL clamp Scroll_Column to [0, Max_Line_Length] after wheel adjustment.
4. WHILE in Wrap_Mode, WHEN the user performs a Shift+wheel scroll, THE Content_Area SHALL ignore the horizontal scroll input and not change Scroll_Column.

### Requirement 5: Viewport Column Calculation

**User Story:** As a user, I want the scrollbar to adapt when I resize the window, so that the scrollable range stays accurate.

#### Acceptance Criteria

1. WHEN the editor window is resized, THE Content_Area SHALL recalculate Viewport_Columns as floor((content_area_pixel_width - 72) / Char_Cell_pixel_width) within 100ms of the resize event completing.
2. WHEN Viewport_Columns changes, THE Horizontal_Scrollbar SHALL update its thumb size to (Viewport_Columns / Max_Line_Length) and its scrollable range to (Max_Line_Length - Viewport_Columns).
3. IF Viewport_Columns becomes greater than or equal to Max_Line_Length after resize, THEN THE Content_Area SHALL hide the Horizontal_Scrollbar and reset Scroll_Column to 0.
4. IF Scroll_Column + Viewport_Columns exceeds Max_Line_Length after resize, THEN THE Content_Area SHALL clamp Scroll_Column to (Max_Line_Length - Viewport_Columns) so that no empty space is shown beyond the longest line.

### Requirement 6: Scroll Position Persistence Across Vertical Scroll

**User Story:** As a user, I want horizontal scroll position to be maintained as I scroll vertically, so that I don't lose my place in wide content.

#### Acceptance Criteria

1. WHILE the user scrolls vertically in No_Wrap_Mode, THE Content_Area SHALL maintain the current Scroll_Column value unchanged.
2. WHEN new lines are fetched from the backend during vertical scroll, THE Content_Area SHALL include the current Scroll_Column as the startColumn parameter in viewport requests so that returned content aligns with the horizontal offset.

### Requirement 7: Integration with Large-Line Chunk Loading

**User Story:** As a user, I want horizontal scrolling to work seamlessly for both normal and large lines, so that I have a unified scrolling experience.

#### Acceptance Criteria

1. WHEN Scroll_Column changes AND large lines (exceeding 65,536 characters) are visible, THE Content_Area SHALL request line chunks covering the Column_Window range (at least 3× the visible column count, centered on the new Scroll_Column position) for each visible large line.
2. WHEN Scroll_Column changes AND only normal lines (65,536 characters or fewer) are visible, THE Content_Area SHALL render content from the already-loaded buffer using substring extraction without issuing any backend request.
3. WHILE a chunk request for a large line is pending, THE Content_Area SHALL display monospace-width blank space of the same character count as the requested range, preserving line height and horizontal layout dimensions.
4. IF a chunk response arrives for a scroll position the user has already scrolled past, THEN THE Content_Area SHALL store the chunk in the cache but SHALL NOT re-render stale content at the current viewport position.
5. WHEN Scroll_Column changes AND the vertical buffer contains a mix of normal and large lines, THE Content_Area SHALL request chunks only for large lines whose total character length exceeds the current Scroll_Column; normal lines and large lines shorter than Scroll_Column SHALL render as empty space with no chunk request.

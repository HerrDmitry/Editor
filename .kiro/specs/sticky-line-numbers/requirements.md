# Requirements Document

## Introduction

When word-wrap is enabled and a long line wraps across multiple visual rows, scrolling can cause the line number in the gutter to scroll off-screen while wrapped content of that line remains visible. This feature makes line numbers "sticky" — the line number stays visible at the top of its line container as long as any part of the wrapped line is within the viewport.

## Glossary

- **Editor**: The Blazor + Photino desktop text editor application
- **Content_Area**: The React component (`ContentArea.tsx`) responsible for rendering file content with virtual scrolling
- **Line_Container**: The flex-row DOM element (`.line-container`) holding a line number and its content
- **Line_Number_Gutter**: The fixed-width column (60px) displaying line numbers for each physical line
- **Wrapped_Line**: A physical line whose content spans multiple visual rows due to word-wrap being enabled
- **Viewport**: The visible scrollable area of the content column
- **Sticky_Position**: CSS `position: sticky` behavior where an element remains fixed relative to its scroll container while its parent is in view

## Requirements

### Requirement 1: Sticky Line Number in Wrap Mode

**User Story:** As a developer reading a file with long wrapped lines, I want the line number to remain visible while any part of that line is in the viewport, so that I always know which line I am looking at.

#### Acceptance Criteria

1. WHILE wrap mode is enabled AND a Wrapped_Line is partially scrolled such that its first visual row is above the Viewport top edge, THE Line_Number_Gutter for that line SHALL remain pinned to the top of the Viewport (top offset 0px relative to the scroll container) and SHALL NOT extend beyond the bottom edge of its Line_Container
2. WHEN the entire Line_Container scrolls below the Viewport top edge (line becomes fully visible), THE Line_Number_Gutter SHALL return to its default position at the top of the Line_Container without visual jump or flicker during the transition
3. WHEN the Line_Container scrolls entirely above the Viewport (line is no longer visible), THE Line_Number_Gutter SHALL scroll away with its Line_Container
4. WHILE wrap mode is enabled AND a Physical_Line occupies only a single visual row, THE Line_Number_Gutter for that line SHALL NOT enter sticky state (the line scrolls normally with its Line_Container)

### Requirement 2: No Behavior Change in Non-Wrap Mode

**User Story:** As a developer using the editor without word-wrap, I want line number positioning to remain unchanged, so that the feature does not introduce visual regressions.

#### Acceptance Criteria

1. WHILE wrap mode is disabled, THE Line_Number_Gutter SHALL remain positioned at the top of its Line_Container using default flow positioning (no `position: sticky` and no `top` offset applied)
2. WHILE wrap mode is disabled, THE Line_Container height SHALL remain equal to the fixed line height (20px) for every line regardless of content length
3. WHILE wrap mode is disabled AND the user scrolls the Viewport, THE Content_Area SHALL NOT apply any sticky-positioning logic or scroll-driven position recalculation to Line_Number_Gutter elements

### Requirement 3: Visual Consistency of Sticky Line Numbers

**User Story:** As a developer, I want sticky line numbers to look consistent with non-sticky line numbers, so that the gutter appears uniform during scrolling.

#### Acceptance Criteria

1. THE Line_Number_Gutter in sticky state SHALL match the actual rendered width of the non-sticky gutter (nominally 60px), font size (14px), color (#858585), line-height (20px), and right padding (12px) as in non-sticky state
2. THE Line_Number_Gutter in sticky state SHALL have a background color matching the editor background (#1e1e1e) to prevent content from showing through behind the number
3. WHILE a Line_Number_Gutter is in sticky state, THE Line_Number_Gutter SHALL remain visually contained within its parent Line_Container bounding box and SHALL NOT extend into or render on top of adjacent Line_Container elements

### Requirement 4: Correct Sticky Behavior Across Multiple Wrapped Lines

**User Story:** As a developer scrolling through a file with several consecutive long lines, I want each line number to stick independently, so that the visible line number always corresponds to the topmost partially-visible wrapped line.

#### Acceptance Criteria

1. WHILE wrap mode is enabled AND multiple consecutive Wrapped_Lines are rendered in the Content_Area, THE Content_Area SHALL apply sticky positioning to at most one Line_Number_Gutter at any time — the one belonging to the topmost Wrapped_Line whose first visual row is scrolled above the Viewport top edge
2. IF no Wrapped_Line has its first visual row above the Viewport top edge, THEN THE Content_Area SHALL display all Line_Number_Gutters in their default (non-sticky) positions
3. WHEN the bottom edge of a sticky line's Line_Container scrolls to the Viewport top edge, THE Content_Area SHALL remove sticky positioning from that line's Line_Number_Gutter and apply sticky positioning to the next Wrapped_Line's Line_Number_Gutter, prioritizing smooth animation over strict single-frame timing when browser performance constraints require multi-frame transitions
4. WHILE a sticky transition occurs between consecutive Wrapped_Lines, THE Line_Number_Gutter elements SHALL not shift horizontally or vertically beyond their expected gutter column position (0px horizontal offset from gutter left edge)

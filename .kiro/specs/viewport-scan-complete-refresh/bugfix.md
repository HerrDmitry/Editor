# Bugfix Requirements Document

## Introduction

When a large file is opened, the early-content-display feature sends a partial `FileOpenedResponse` after scanning the first ~256 KB, allowing the frontend to display initial content. The scan then continues in the background. Upon scan completion, a final `FileOpenedResponse` (with `isPartial: false`) is sent containing the definitive total line count. However, no viewport content update is triggered after scan completion — the frontend receives the updated metadata but never re-fetches or receives refreshed viewport content. As a result, the displayed text remains limited to what was fetched during the partial phase, even though the full file is now indexed and available.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a large file scan completes (transitioning from partial to final metadata) THEN the system sends only a `FileOpenedResponse` with updated metadata but does not trigger a viewport content refresh to the frontend

1.2 WHEN the frontend receives the final `FileOpenedResponse` after scan completion THEN the system does not push updated viewport content, leaving the display showing only the lines fetched during the partial scan phase

### Expected Behavior (Correct)

2.1 WHEN a large file scan completes (transitioning from partial to final metadata) THEN the backend SHALL push a `ViewportResponse` after the final `FileOpenedResponse` as a safety net for viewport-protocol consumers

2.2 WHEN the frontend receives the final `FileOpenedResponse` (non-partial, non-refresh) for the same file that was partially loaded THEN the frontend SHALL re-request its current buffer range via `sendRequestLines` so the displayed content is refreshed with fully-indexed file data

2.3 WHEN the frontend receives the final `FileOpenedResponse` for a different file (small file, first open) THEN the frontend SHALL perform a full reset and initial line request as before (no change)

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a small file (≤256 KB) is opened THEN the system SHALL CONTINUE TO send a single `FileOpenedResponse` followed by normal viewport request/response flow without any additional viewport push

3.2 WHEN the user scrolls or requests a viewport during the scanning phase THEN the system SHALL CONTINUE TO serve viewport content from the partially-indexed lines as before

3.3 WHEN an external file modification triggers a refresh cycle THEN the system SHALL CONTINUE TO send a `FileOpenedResponse` with `isRefresh: true` and the frontend SHALL CONTINUE TO re-request viewport content as before

3.4 WHEN the scan is cancelled due to opening a new file THEN the system SHALL CONTINUE TO cancel cleanly without sending a viewport update for the cancelled file

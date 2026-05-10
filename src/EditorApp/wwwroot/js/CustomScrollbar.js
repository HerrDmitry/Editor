"use strict";
// CustomScrollbar.tsx — compiled by tsc to wwwroot/js/CustomScrollbar.js
// No module imports — React is a global from the UMD script.
/** Minimum thumb size in pixels to keep it grabbable. */
const MIN_THUMB_SIZE = 20;
function CustomScrollbar({ range, position, viewportSize, orientation = 'vertical', onPositionChange }) {
    const trackRef = React.useRef(null);
    const [isDragging, setIsDragging] = React.useState(false);
    const dragStartRef = React.useRef({ mousePos: 0, thumbOffsetAtStart: 0 });
    const isHorizontal = orientation === 'horizontal';
    // Suppress onPositionChange during external (programmatic) position updates.
    // We only call onPositionChange when the user is actively dragging.
    const isUserDragging = React.useRef(false);
    // Calculate thumb size proportional to viewport/range ratio.
    const getThumbSize = (trackSize) => {
        if (range <= 0)
            return trackSize;
        return Math.max(MIN_THUMB_SIZE, (viewportSize / (range + viewportSize)) * trackSize);
    };
    // Calculate thumb offset position from the current position value.
    const getThumbOffset = (trackSize, thumbSize) => {
        if (range <= 0)
            return 0;
        const scrollableTrack = trackSize - thumbSize;
        if (scrollableTrack <= 0)
            return 0;
        const fraction = position / range;
        return Math.max(0, Math.min(fraction * scrollableTrack, scrollableTrack));
    };
    // Get current metrics — use clientWidth for horizontal, clientHeight for vertical
    const trackSize = trackRef.current
        ? (isHorizontal ? trackRef.current.clientWidth : trackRef.current.clientHeight)
        : 0;
    const thumbSize = getThumbSize(trackSize);
    const thumbOffset = getThumbOffset(trackSize, thumbSize);
    // Thumb dragging: mousedown on thumb
    const handleThumbMouseDown = (e) => {
        e.preventDefault();
        e.stopPropagation();
        setIsDragging(true);
        isUserDragging.current = true;
        const mousePos = isHorizontal ? e.clientX : e.clientY;
        dragStartRef.current = { mousePos, thumbOffsetAtStart: thumbOffset };
    };
    // Document-level mousemove/mouseup for dragging
    React.useEffect(() => {
        if (!isDragging)
            return;
        const handleMouseMove = (e) => {
            e.preventDefault();
            if (!trackRef.current)
                return;
            const currentTrackSize = isHorizontal ? trackRef.current.clientWidth : trackRef.current.clientHeight;
            const currentThumbSize = getThumbSize(currentTrackSize);
            const scrollableTrack = currentTrackSize - currentThumbSize;
            if (scrollableTrack <= 0)
                return;
            const currentMousePos = isHorizontal ? e.clientX : e.clientY;
            const delta = currentMousePos - dragStartRef.current.mousePos;
            const newThumbOffset = Math.max(0, Math.min(dragStartRef.current.thumbOffsetAtStart + delta, scrollableTrack));
            // Calculate position: linear mapping from thumbOffset to range
            // Snap to exact endpoints to avoid floating-point near-misses
            let calculatedPosition;
            if (newThumbOffset <= 0) {
                calculatedPosition = 0;
            }
            else if (newThumbOffset >= scrollableTrack) {
                calculatedPosition = range;
            }
            else {
                calculatedPosition = (newThumbOffset / scrollableTrack) * range;
            }
            if (onPositionChange) {
                onPositionChange(calculatedPosition);
            }
        };
        const handleMouseUp = () => {
            setIsDragging(false);
            isUserDragging.current = false;
        };
        document.addEventListener('mousemove', handleMouseMove);
        document.addEventListener('mouseup', handleMouseUp);
        return () => {
            document.removeEventListener('mousemove', handleMouseMove);
            document.removeEventListener('mouseup', handleMouseUp);
        };
    }, [isDragging, range, viewportSize, onPositionChange, isHorizontal]);
    const thumbClassName = 'custom-scrollbar__thumb' + (isDragging ? ' custom-scrollbar__thumb--active' : '');
    const containerClassName = 'custom-scrollbar' + (isHorizontal ? ' custom-scrollbar--horizontal' : '');
    // Style: vertical uses top/height, horizontal uses left/width
    const thumbStyle = isHorizontal
        ? { left: thumbOffset, width: thumbSize }
        : { top: thumbOffset, height: thumbSize };
    return (React.createElement("div", { className: containerClassName },
        React.createElement("div", { className: "custom-scrollbar__track", ref: trackRef }, React.createElement('div', {
            className: thumbClassName,
            style: thumbStyle,
            onMouseDown: handleThumbMouseDown,
        }))));
}
// Expose on window
window.CustomScrollbar = CustomScrollbar;

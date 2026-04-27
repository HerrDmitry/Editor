"use strict";
// CustomScrollbar.tsx — compiled by tsc to wwwroot/js/CustomScrollbar.js
// No module imports — React is a global from the UMD script.
/** Minimum thumb height in pixels to keep it grabbable. */
const MIN_THUMB_HEIGHT = 20;
function CustomScrollbar({ range, position, viewportSize, onPositionChange }) {
    const trackRef = React.useRef(null);
    const [isDragging, setIsDragging] = React.useState(false);
    const dragStartRef = React.useRef({ mouseY: 0, thumbTopAtStart: 0 });
    // Suppress onPositionChange during external (programmatic) position updates.
    // We only call onPositionChange when the user is actively dragging.
    const isUserDragging = React.useRef(false);
    // Calculate thumb height proportional to viewport/range ratio.
    const getThumbHeight = (trackHeight) => {
        if (range <= 0)
            return trackHeight;
        return Math.max(MIN_THUMB_HEIGHT, (viewportSize / (range + viewportSize)) * trackHeight);
    };
    // Calculate thumb top position from the current position value.
    const getThumbTop = (trackHeight, thumbHeight) => {
        if (range <= 0)
            return 0;
        const scrollableTrack = trackHeight - thumbHeight;
        if (scrollableTrack <= 0)
            return 0;
        const fraction = position / range;
        return Math.max(0, Math.min(fraction * scrollableTrack, scrollableTrack));
    };
    // Get current metrics
    const trackHeight = trackRef.current ? trackRef.current.clientHeight : 0;
    const thumbHeight = getThumbHeight(trackHeight);
    const thumbTop = getThumbTop(trackHeight, thumbHeight);
    // Thumb dragging: mousedown on thumb
    const handleThumbMouseDown = (e) => {
        e.preventDefault();
        e.stopPropagation();
        setIsDragging(true);
        isUserDragging.current = true;
        dragStartRef.current = { mouseY: e.clientY, thumbTopAtStart: thumbTop };
    };
    // Document-level mousemove/mouseup for dragging
    React.useEffect(() => {
        if (!isDragging)
            return;
        const handleMouseMove = (e) => {
            e.preventDefault();
            if (!trackRef.current)
                return;
            const currentTrackHeight = trackRef.current.clientHeight;
            const currentThumbHeight = getThumbHeight(currentTrackHeight);
            const scrollableTrack = currentTrackHeight - currentThumbHeight;
            if (scrollableTrack <= 0)
                return;
            const deltaY = e.clientY - dragStartRef.current.mouseY;
            const newThumbTop = Math.max(0, Math.min(dragStartRef.current.thumbTopAtStart + deltaY, scrollableTrack));
            // Calculate position: linear mapping from thumbTop to range
            const calculatedPosition = (newThumbTop / scrollableTrack) * range;
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
    }, [isDragging, range, viewportSize, onPositionChange]);
    const thumbClassName = 'custom-scrollbar__thumb' + (isDragging ? ' custom-scrollbar__thumb--active' : '');
    return (React.createElement("div", { className: "custom-scrollbar" },
        React.createElement("div", { className: "custom-scrollbar__track", ref: trackRef }, React.createElement('div', {
            className: thumbClassName,
            style: { top: thumbTop, height: thumbHeight },
            onMouseDown: handleThumbMouseDown,
        }))));
}
// Expose on window
window.CustomScrollbar = CustomScrollbar;

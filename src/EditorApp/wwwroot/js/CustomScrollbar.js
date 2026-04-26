"use strict";
// CustomScrollbar.tsx — compiled by tsc to wwwroot/js/CustomScrollbar.js
// No module imports — React is a global from the UMD script.
function CustomScrollbar({ totalLines, visibleLineCount, currentTopLine, onScrollToLine }) {
    const trackRef = React.useRef(null);
    const [isDragging, setIsDragging] = React.useState(false);
    const dragStartRef = React.useRef({ mouseY: 0, startLine: 0 });
    // Calculate thumb position and size based on track height
    const getThumbMetrics = () => {
        if (!trackRef.current || totalLines <= 0) {
            return { thumbTop: 0, thumbHeight: 20 };
        }
        const trackHeight = trackRef.current.clientHeight;
        const thumbHeight = Math.max(20, (visibleLineCount / totalLines) * trackHeight);
        const scrollableTrack = trackHeight - thumbHeight;
        const maxScrollLine = Math.max(1, totalLines - visibleLineCount);
        const thumbTop = (currentTopLine / maxScrollLine) * scrollableTrack;
        return { thumbTop: Math.max(0, Math.min(thumbTop, scrollableTrack)), thumbHeight };
    };
    const { thumbTop, thumbHeight } = getThumbMetrics();
    // Thumb dragging: mousedown on thumb
    const handleThumbMouseDown = (e) => {
        e.preventDefault();
        e.stopPropagation();
        setIsDragging(true);
        dragStartRef.current = { mouseY: e.clientY, startLine: currentTopLine };
    };
    // Document-level mousemove/mouseup for dragging
    React.useEffect(() => {
        if (!isDragging)
            return;
        const handleMouseMove = (e) => {
            e.preventDefault();
            if (!trackRef.current)
                return;
            const trackHeight = trackRef.current.clientHeight;
            const thumbH = Math.max(20, (visibleLineCount / totalLines) * trackHeight);
            const scrollableTrack = trackHeight - thumbH;
            if (scrollableTrack <= 0)
                return;
            const deltaY = e.clientY - dragStartRef.current.mouseY;
            const maxScrollLine = Math.max(1, totalLines - visibleLineCount);
            const lineDelta = (deltaY / scrollableTrack) * maxScrollLine;
            const targetLine = Math.round(dragStartRef.current.startLine + lineDelta);
            const clamped = Math.max(0, Math.min(targetLine, totalLines - visibleLineCount));
            onScrollToLine(clamped);
        };
        const handleMouseUp = () => {
            setIsDragging(false);
        };
        document.addEventListener('mousemove', handleMouseMove);
        document.addEventListener('mouseup', handleMouseUp);
        return () => {
            document.removeEventListener('mousemove', handleMouseMove);
            document.removeEventListener('mouseup', handleMouseUp);
        };
    }, [isDragging, totalLines, visibleLineCount, onScrollToLine]);
    // Track click: page up/down
    const handleTrackClick = (e) => {
        if (!trackRef.current)
            return;
        const trackRect = trackRef.current.getBoundingClientRect();
        const clickY = e.clientY - trackRect.top;
        if (clickY < thumbTop) {
            // Click above thumb — page up
            const targetLine = Math.max(0, currentTopLine - visibleLineCount);
            onScrollToLine(targetLine);
        }
        else if (clickY > thumbTop + thumbHeight) {
            // Click below thumb — page down
            const targetLine = Math.min(totalLines - visibleLineCount, currentTopLine + visibleLineCount);
            onScrollToLine(Math.max(0, targetLine));
        }
    };
    const thumbClassName = 'custom-scrollbar__thumb' + (isDragging ? ' custom-scrollbar__thumb--active' : '');
    return (React.createElement("div", { className: "custom-scrollbar" },
        React.createElement("div", { className: "custom-scrollbar__track", ref: trackRef, onClick: handleTrackClick }, React.createElement('div', {
            className: thumbClassName,
            style: { top: thumbTop, height: thumbHeight },
            onMouseDown: handleThumbMouseDown,
        }))));
}
// Expose on window
window.CustomScrollbar = CustomScrollbar;

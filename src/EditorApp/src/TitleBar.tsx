// TitleBar.tsx — compiled by tsc to wwwroot/js/TitleBar.js
// No module imports — React is a global from the UMD script.

interface TitleBarProps {
  title: string;
}

function TitleBar({ title }: TitleBarProps) {
  return (
    <div className="title-bar" role="banner">
      <span className="title-bar__text">{title}</span>
    </div>
  );
}

// Expose on window
(window as any).TitleBar = TitleBar;

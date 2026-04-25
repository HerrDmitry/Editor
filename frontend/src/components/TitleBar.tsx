import './TitleBar.css';

export interface TitleBarProps {
  title: string;
}

function TitleBar({ title }: TitleBarProps) {
  return (
    <div className="title-bar" role="banner">
      <span className="title-bar__text">{title}</span>
    </div>
  );
}

export default TitleBar;

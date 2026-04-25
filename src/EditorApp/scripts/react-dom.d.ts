// Minimal ReactDOM type declarations for standalone (non-npm) usage.
// ReactDOM is loaded as a global via <script> tag (UMD build).

declare namespace ReactDOM {
  interface Root {
    render(element: React.ReactNode): void;
    unmount(): void;
  }

  function createRoot(container: Element | null): Root;
  function render(element: React.ReactNode, container: Element | null): void;
}

declare var ReactDOM: typeof ReactDOM;

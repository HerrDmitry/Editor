// Minimal React type declarations for standalone (non-npm) usage.
// React is loaded as a global via <script> tag (UMD build).

declare namespace React {
  type Key = string | number;

  interface RefObject<T> {
    readonly current: T | null;
  }

  interface MutableRefObject<T> {
    current: T;
  }

  type Ref<T> = RefObject<T> | ((instance: T | null) => void) | null;

  type SetStateAction<S> = S | ((prevState: S) => S);
  type Dispatch<A> = (value: A) => void;

  type DependencyList = ReadonlyArray<unknown>;

  interface ReactElement<P = any> {
    type: string | ComponentType<P>;
    props: P;
    key: Key | null;
  }

  type ReactNode = ReactElement | string | number | boolean | null | undefined | ReactNode[];

  type FC<P = {}> = (props: P) => ReactElement | null;
  type ComponentType<P = {}> = FC<P>;

  type JSXElementConstructor<P> = (props: P) => ReactElement<any> | null;

  // Hooks
  function useState<S>(initialState: S | (() => S)): [S, Dispatch<SetStateAction<S>>];
  function useEffect(effect: () => void | (() => void), deps?: DependencyList): void;
  function useLayoutEffect(effect: () => void | (() => void), deps?: DependencyList): void;
  function useCallback<T extends (...args: any[]) => any>(callback: T, deps: DependencyList): T;
  function useRef<T>(initialValue: T): MutableRefObject<T>;
  function useRef<T>(initialValue: T | null): RefObject<T>;
  function useMemo<T>(factory: () => T, deps: DependencyList): T;
  function useContext<T>(context: Context<T>): T;
  function useReducer<R extends Reducer<any, any>>(
    reducer: R,
    initialState: ReducerState<R>,
  ): [ReducerState<R>, Dispatch<ReducerAction<R>>];

  type Reducer<S, A> = (prevState: S, action: A) => S;
  type ReducerState<R extends Reducer<any, any>> = R extends Reducer<infer S, any> ? S : never;
  type ReducerAction<R extends Reducer<any, any>> = R extends Reducer<any, infer A> ? A : never;

  interface Context<T> {
    Provider: ComponentType<{ value: T; children?: ReactNode }>;
    Consumer: ComponentType<{ children: (value: T) => ReactNode }>;
  }

  function createContext<T>(defaultValue: T): Context<T>;
  function createElement(
    type: string | ComponentType<any>,
    props?: any,
    ...children: ReactNode[]
  ): ReactElement;
  function Fragment(props: { children?: ReactNode }): ReactElement;

  // Event types
  interface SyntheticEvent<T = Element> {
    currentTarget: T;
    target: EventTarget;
    preventDefault(): void;
    stopPropagation(): void;
  }

  interface ChangeEvent<T = Element> extends SyntheticEvent<T> {
    target: EventTarget & T;
  }

  interface MouseEvent<T = Element> extends SyntheticEvent<T> {
    clientX: number;
    clientY: number;
    button: number;
  }

  interface KeyboardEvent<T = Element> extends SyntheticEvent<T> {
    key: string;
    code: string;
    ctrlKey: boolean;
    metaKey: boolean;
    shiftKey: boolean;
    altKey: boolean;
  }

  interface UIEvent<T = Element> extends SyntheticEvent<T> {
  }

  interface WheelEvent<T = Element> extends SyntheticEvent<T> {
    deltaY: number;
    deltaX: number;
    shiftKey: boolean;
    preventDefault(): void;
  }

  // HTML attributes
  interface HTMLAttributes<T> {
    key?: Key;
    ref?: Ref<T>;
    className?: string;
    id?: string;
    style?: CSSProperties;
    role?: string;
    tabIndex?: number;
    onClick?: (event: MouseEvent<T>) => void;
    onKeyDown?: (event: KeyboardEvent<T>) => void;
    onChange?: (event: ChangeEvent<T>) => void;
    onScroll?: (event: UIEvent<T>) => void;
    onWheel?: (event: WheelEvent<T>) => void;
    children?: ReactNode;
    'aria-label'?: string;
    'aria-hidden'?: boolean | 'true' | 'false';
  }

  interface CSSProperties {
    [key: string]: string | number | undefined;
  }

  interface ButtonHTMLAttributes<T> extends HTMLAttributes<T> {
    type?: 'button' | 'submit' | 'reset';
    disabled?: boolean;
  }
}

// JSX namespace for TSX support
declare namespace JSX {
  interface Element extends React.ReactElement {}
  interface IntrinsicElements {
    div: React.HTMLAttributes<HTMLDivElement>;
    span: React.HTMLAttributes<HTMLSpanElement>;
    p: React.HTMLAttributes<HTMLParagraphElement>;
    pre: React.HTMLAttributes<HTMLPreElement>;
    button: React.ButtonHTMLAttributes<HTMLButtonElement>;
    h1: React.HTMLAttributes<HTMLHeadingElement>;
    h2: React.HTMLAttributes<HTMLHeadingElement>;
    h3: React.HTMLAttributes<HTMLHeadingElement>;
    a: React.HTMLAttributes<HTMLAnchorElement> & { href?: string; target?: string };
    input: React.HTMLAttributes<HTMLInputElement> & { type?: string; value?: string; placeholder?: string; checked?: boolean; disabled?: boolean; min?: number | string; max?: number | string; step?: number | string };
    label: React.HTMLAttributes<HTMLLabelElement> & { htmlFor?: string };
    ul: React.HTMLAttributes<HTMLUListElement>;
    li: React.HTMLAttributes<HTMLLIElement>;
    ol: React.HTMLAttributes<HTMLOListElement>;
    img: React.HTMLAttributes<HTMLImageElement> & { src?: string; alt?: string };
    [elemName: string]: any;
  }
}

// Make React available as a global
declare var React: typeof React;

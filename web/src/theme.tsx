import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

// Light/dark theme. The active theme is reflected as `data-theme` on <html>; the CSS
// variables for each theme live in index.css (:root vs :root[data-theme='dark']).
// The choice is persisted in localStorage; the default is always light — a first visit
// (no stored choice) lands on light regardless of the OS preference. This mirrors the
// pre-hydration script in index.html, which must stay in sync.

type Theme = 'light' | 'dark';

const THEME_KEY = 'ulbra-one.theme';

function getInitialTheme(): Theme {
  if (typeof window === 'undefined') return 'light';
  return window.localStorage.getItem(THEME_KEY) === 'dark' ? 'dark' : 'light';
}

interface ThemeContextValue {
  theme: Theme;
  toggle: () => void;
  setTheme: (theme: Theme) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(getInitialTheme);

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    try {
      window.localStorage.setItem(THEME_KEY, theme);
    } catch {
      // Private mode / storage disabled: theme still applies for this session.
    }
  }, [theme]);

  const toggle = () => setTheme((t) => (t === 'dark' ? 'light' : 'dark'));

  return <ThemeContext.Provider value={{ theme, toggle, setTheme }}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used within a ThemeProvider');
  return ctx;
}

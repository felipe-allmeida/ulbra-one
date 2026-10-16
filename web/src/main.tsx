import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import App from './App.tsx';
import { auth } from './api';
import { initClarity } from './analytics';
import { ToastProvider } from './toast';
import { ThemeProvider } from './theme';
import { ErrorBoundary } from './ErrorBoundary';
import { queryClient } from './queryClient';
import './i18n';
import './index.css';

// A stale/expired JWT survives deploys in localStorage and would otherwise "log in"
// the user into /app (→ white screen). Purge it up front so a dead token never lingers.
if (auth.token && !auth.hasValidToken()) auth.clear();

// Clarity só em produção — ver src/analytics.ts. Roda aqui (e não no <head>) porque precisa do
// window.__ENV__ que o env.js publica logo antes deste módulo.
initClarity();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ErrorBoundary>
      <ThemeProvider>
        <QueryClientProvider client={queryClient}>
          <BrowserRouter>
            <ToastProvider>
              <App />
            </ToastProvider>
          </BrowserRouter>
          {import.meta.env.DEV && <ReactQueryDevtools initialIsOpen={false} />}
        </QueryClientProvider>
      </ThemeProvider>
    </ErrorBoundary>
  </StrictMode>,
);

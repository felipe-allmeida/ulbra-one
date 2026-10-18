import { Component, type ErrorInfo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';

/** Full-page fallback shown when a render-time crash bubbles up to the boundary. */
function ErrorFallback({ onReset }: { onReset: () => void }) {
  const { t } = useTranslation();
  return (
    <div className='error-fallback' role='alert'>
      <div className='error-fallback-card'>
        <span className='error-fallback-icon'>
          <AlertTriangle />
        </span>
        <h1 className='error-fallback-title'>{t('errors.boundary.title')}</h1>
        <p className='error-fallback-message'>{t('errors.boundary.message')}</p>
        <div className='error-fallback-actions'>
          <button className='btn btn-brand' onClick={onReset}>
            {t('errors.boundary.retry')}
          </button>
          <a className='btn' href='/'>
            {t('errors.boundary.home')}
          </a>
        </div>
      </div>
    </div>
  );
}

interface Props {
  children: ReactNode;
}
interface State {
  hasError: boolean;
}

/**
 * Catches render-time errors in its subtree and shows a friendly fallback.
 * Async/fetch errors are handled separately via getErrorMessage — they never
 * reach a React error boundary.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Surface the crash in the console for diagnostics; no telemetry sink yet.
    console.error('Render error caught by ErrorBoundary:', error, info);
  }

  reset = () => this.setState({ hasError: false });

  render() {
    if (this.state.hasError) return <ErrorFallback onReset={this.reset} />;
    return this.props.children;
  }
}

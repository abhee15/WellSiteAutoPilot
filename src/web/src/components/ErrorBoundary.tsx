import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import { t } from '../localization';
import { ActionButton } from './ActionButton';
import './errorBoundary.css';

type Props = {
  /** `app` replaces the whole screen and offers a reload. `page` keeps the shell and navigation, and offers a retry. */
  scope: 'app' | 'page';
  children: ReactNode;
};
type State = { failed: boolean; message: string };

/**
 * Stops one rendering failure from leaving a blank screen. The page boundary sits inside the shell, so navigation
 * and the account menu keep working; changing the key (the section) gives the next page a fresh start. The outer
 * app boundary is the last resort and depends on nothing but the message catalog and shared styles.
 * Only the message is shown, never a stack: the full error goes to the browser console for support.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { failed: false, message: '' };
  private heading: HTMLHeadingElement | null = null;

  static getDerivedStateFromError(error: unknown): State {
    return { failed: true, message: error instanceof Error ? error.message : String(error) };
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    console.error('Rendering failed (' + this.props.scope + ')', error, info.componentStack);
  }

  // Say so to keyboard and screen-reader users: focus moves to the explanation when it first appears. A failure while
  // the boundary is first mounting commits as a mount, not an update, so both are handled.
  componentDidMount() {
    if (this.state.failed) this.heading?.focus();
  }

  componentDidUpdate(_previous: Props, previousState: State) {
    if (this.state.failed && !previousState.failed) this.heading?.focus();
  }

  private retry = () => this.setState({ failed: false, message: '' });

  render() {
    if (!this.state.failed) return this.props.children;
    const app = this.props.scope === 'app';
    const Heading = app ? 'h1' : 'h2';
    const body = (
      <div className="error-boundary" role="alert">
        <Heading ref={element => { this.heading = element as HTMLHeadingElement | null; }} tabIndex={-1}>
          {t('errorBoundary.title')}
        </Heading>
        <p>{t(app ? 'errorBoundary.appMessage' : 'errorBoundary.pageMessage')}</p>
        <details>
          <summary>{t('errorBoundary.details')}</summary>
          <code>{this.state.message || t('errorBoundary.noDetails')}</code>
        </details>
        <div>
          <ActionButton variant="primary" label={t(app ? 'errorBoundary.reload' : 'errorBoundary.retry')}
            onClick={app ? () => window.location.reload() : this.retry} />
        </div>
      </div>
    );
    return app ? <main className="error-boundary-app">{body}</main> : body;
  }
}

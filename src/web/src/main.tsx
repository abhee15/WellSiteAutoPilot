import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { ErrorBoundary } from './components/ErrorBoundary';
import { LiveUpdatesProvider } from './live/LiveUpdates';
import '@fontsource/roboto/latin-400.css';
import '@fontsource/roboto/latin-500.css';
import '@fontsource/roboto/latin-700.css';
import './tokens.css';
import './styles.css';

const root = document.getElementById('root');

if (!root) {
  throw new Error('Application root element was not found.');
}

createRoot(root).render(
  <StrictMode>
    <ErrorBoundary scope="app">
      <LiveUpdatesProvider>
        <App />
      </LiveUpdatesProvider>
    </ErrorBoundary>
  </StrictMode>
);

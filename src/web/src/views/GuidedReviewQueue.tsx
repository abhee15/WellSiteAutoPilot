import { useEffect, useState } from 'react';
import { listPendingGuidedModuleSubmissions } from '../api/generated/v1/sdk.gen';
import type { GuidedModuleReviewItemResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { ContextLink } from '../components/ContextLink';
import { PanelLoading } from '../components/LoadingIndicator';
import { Notice } from '../components/Notice';
import { formatTimestamp } from '../format';
import { t } from '../localization';
import { hashFor } from '../routing';

const pageSize = 25;

export function GuidedReviewQueue() {
  const [offset, setOffset] = useState(0);
  const [items, setItems] = useState<GuidedModuleReviewItemResponse[] | null>(null);
  const [retry, setRetry] = useState(0);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    void listPendingGuidedModuleSubmissions({
      query: { offset, limit: pageSize + 1 },
      signal: controller.signal,
    })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        if (!data) throw new Error();
        setItems(data);
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [offset, retry]);

  function move(nextOffset: number) {
    setItems(null);
    setOffset(nextOffset);
  }

  if (failed) {
    return (
      <>
        <Notice tone="error">{t('guided.queueError')}</Notice>
        <ActionButton
          label={t('guided.retryReviews')}
          onClick={() => {
            setFailed(false);
            setRetry((previous) => previous + 1);
          }}
        />
      </>
    );
  }
  if (items === null) return <PanelLoading label={t('guided.loadingReviews')} />;

  return (
    <>
      {items.length === 0 ? (
        <p>{t('guided.queueEmpty')}</p>
      ) : (
        <ul>
          {items.slice(0, pageSize).map((item) => (
            <li key={item.draftId + '/' + item.revision}>
              <ContextLink
                appearance="record"
                href={hashFor('authoring', item.draftId + '/' + item.revision)}
                label={t('guided.queueItem', { name: item.displayName ?? '', revision: item.revision ?? '' })}
              />
              {' · '}
              {formatTimestamp(item.submittedAtUtc)}
            </li>
          ))}
        </ul>
      )}
      {(offset > 0 || items.length > pageSize) && (
        <div className="guided-authoring-actions">
          {offset > 0 && <ActionButton label={t('guided.previousReviews')} onClick={() => move(offset - pageSize)} />}
          {items.length > pageSize && (
            <ActionButton label={t('guided.nextReviews')} onClick={() => move(offset + pageSize)} />
          )}
        </div>
      )}
    </>
  );
}

import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { inspectSurfaceCards, surfaceCardPlot } from './cardInspection';

export function SurfaceCardPreview({ schemaId, json }: { schemaId: string; json: string }) {
  const cards = inspectSurfaceCards(schemaId, json);
  if (!cards) return null;
  const number = new Intl.NumberFormat(undefined, { maximumSignificantDigits: 6 });
  return <div className="surface-cards">
    <p>{t('execution.cardPreviewHint')}</p>
    {cards.map(card => {
      const plot = surfaceCardPlot(card);
      return <details key={card.id} className="surface-card">
        <summary>{card.kind} · {t('execution.cardSamples', { count: card.position.length })} · {formatDetailTimestamp(card.capturedAtUtc)}</summary>
        <dl className="run-input-facts">
          <div><dt>{t('execution.cardIdentity')}</dt><dd>{card.id}</dd></div>
          <div><dt>{t('execution.cardRepresentation')}</dt><dd>{card.representation}</dd></div>
          <div><dt>{t('shadow.source')}</dt><dd>{card.source}</dd></div>
        </dl>
        <svg viewBox="-40 0 640 280" role="img" className="surface-card-plot"
          aria-label={t('execution.cardPlotLabel', { kind: card.kind, position: card.positionUnit, load: card.loadUnit })}>
          <path d="M60 30 V230 H500" className="surface-card-axis" />
          <polyline points={plot.points} className="surface-card-line" />
          {plot.minPosition === plot.maxPosition && plot.minLoad === plot.maxLoad &&
            <circle cx="280" cy="130" r="3" className="surface-card-point" />}
          <text x="60" y="18">{t('execution.cardLoad', { unit: card.loadUnit })}</text>
          <text x="280" y="275" textAnchor="middle">{t('execution.cardPosition', { unit: card.positionUnit })}</text>
          <text x="55" y="38" textAnchor="end">{number.format(plot.maxLoad)}</text>
          <text x="55" y="230" textAnchor="end">{number.format(plot.minLoad)}</text>
          <text x="60" y="250">{number.format(plot.minPosition)}</text>
          <text x="500" y="250" textAnchor="end">{number.format(plot.maxPosition)}</text>
        </svg>
      </details>;
    })}
  </div>;
}

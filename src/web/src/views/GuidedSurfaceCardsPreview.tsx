import { useEffect, useRef, useState } from 'react';
import type { GuidedSurfaceCardsPreviewResponse } from '../api/generated/v1/types.gen';
import { SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { formatTimestamp } from '../format';
import { t } from '../localization';

export function GuidedSurfaceCardsPreview({ preview }: { preview: GuidedSurfaceCardsPreviewResponse }) {
  const [selectedIndex, setSelectedIndex] = useState(0);
  const canvas = useRef<HTMLCanvasElement>(null);
  const card = preview.cards?.[selectedIndex];
  const position = card?.position;
  const load = card?.load;
  const minPosition = position ? Math.min(...position) : 0;
  const maxPosition = position ? Math.max(...position) : 0;
  const minLoad = load ? Math.min(...load) : 0;
  const maxLoad = load ? Math.max(...load) : 0;

  useEffect(() => {
    const plot = canvas.current;
    if (!plot || !position?.length || !load?.length) return;
    const context = plot.getContext('2d');
    if (!context) return;
    const margin = 12;
    const width = plot.width - 2 * margin;
    const height = plot.height - 2 * margin;
    const scale = (value: number, min: number, max: number) => (max === min ? 0.5 : (value - min) / (max - min));
    context.clearRect(0, 0, plot.width, plot.height);
    context.strokeStyle = getComputedStyle(plot).color;
    context.lineWidth = 2;
    context.beginPath();
    position.forEach((value, index) => {
      const x = margin + scale(value, minPosition, maxPosition) * width;
      const y = plot.height - margin - scale(load[index], minLoad, maxLoad) * height;
      if (index === 0) context.moveTo(x, y);
      else context.lineTo(x, y);
    });
    context.stroke();
  }, [position, load, minPosition, maxPosition, minLoad, maxLoad]);

  if (!card) return <Notice>{t('guided.surfaceCardsEmpty')}</Notice>;
  return (
    <div className="guided-authoring-review">
      <SelectField
        label={t('guided.surfaceCard')}
        value={selectedIndex}
        onChange={(event) => setSelectedIndex(Number(event.target.value))}
      >
        {preview.cards?.map((item, index) => (
          <option key={item.id} value={index}>
            {item.kind} {index + 1} · {formatTimestamp(item.capturedAtUtc)}
          </option>
        ))}
      </SelectField>
      <figure className="guided-surface-card">
        <canvas ref={canvas} width={640} height={360} role="img" aria-label={t('guided.surfaceCardPlot')} />
        <figcaption>
          {t('guided.surfaceCardPosition')} {minPosition}–{maxPosition} {card.positionUnit} ·{' '}
          {t('guided.surfaceCardLoad')} {minLoad}–{maxLoad} {card.loadUnit} · {card.position?.length}{' '}
          {t('guided.surfaceCardPoints')}
        </figcaption>
      </figure>
      <p>
        {t('guided.surfaceCardQuality')} {preview.quality} · {t('guided.previewRetrieved')}{' '}
        {formatTimestamp(preview.retrievedAtUtc)}
      </p>
    </div>
  );
}

import { ActionButton } from '../components/ActionButton';
import { t } from '../localization';
import type { Analysis, GraphNode, Problem } from './authoring';
import { kindLabel } from './authoringDraft';
import type { NodeKind, Selection } from './authoringDraft';

type Props = {
  analysis: Analysis | null;
  problems: Problem[];
  selection: Selection;
  valueOf: Map<string, string>;
  onSelect: (kind: NodeKind, name: string) => void;
  onAdd: (kind: NodeKind) => void;
};

const columns: {
  title: 'authoring.flowReads' | 'authoring.flowSteps' | 'authoring.flowOutputs';
  kinds: GraphNode['kind'][];
  add: NodeKind;
}[] = [
  { title: 'authoring.flowReads', kinds: ['input', 'parameter'], add: 'input' },
  { title: 'authoring.flowSteps', kinds: ['step'], add: 'step' },
  { title: 'authoring.flowOutputs', kinds: ['output'], add: 'output' },
];

/**
 * The module as three columns: what it reads, how it decides, what it produces. It is a map for understanding and
 * navigation, so each box opens that part of the module for editing and the selected box marks what it reads and what
 * reads it. Limits and checks live on the outputs, so nothing here can bypass them.
 */
export function AuthoringFlow({ analysis, problems, selection, valueOf, onSelect, onAdd }: Props) {
  if (!analysis) return <p className="connection-help">{t('authoring.flowWaiting')}</p>;
  const current =
    'name' in selection
      ? analysis.graph.find((node) => node.kind === selection.kind && node.name === selection.name)
      : undefined;
  const relation = (node: GraphNode) => {
    if (!current) return undefined;
    if (node === current) return 'self';
    if (current.dependsOn.includes(node.name)) return 'feeds';
    if (node.dependsOn.includes(current.name)) return 'fed';
    return undefined;
  };

  return (
    <div className="authoring-flow" role="group" aria-label={t('authoring.flowTitle')}>
      {columns.map((column) => (
        <div key={column.title} className="authoring-flow-column">
          <p className="authoring-flow-heading">{t(column.title)}</p>
          {analysis.graph
            .filter((node) => column.kinds.includes(node.kind))
            .map((node) => {
              const own = problems.filter((problem) => problem.node === node.name);
              return (
                <button
                  key={`${node.kind}-${node.name}`}
                  type="button"
                  className="authoring-flow-node"
                  data-kind={node.kind}
                  data-relation={relation(node)}
                  data-problem={
                    own.some((problem) => problem.severity === 'Error') ? 'error' : own.length ? 'warning' : undefined
                  }
                  aria-current={current === node}
                  onClick={() => onSelect(node.kind, node.name)}
                >
                  <span className="authoring-flow-name">{node.name}</span>
                  <small>
                    {kindLabel[node.kind]}
                    {valueOf.get(node.name) ? ` · ${valueOf.get(node.name)}` : ''}
                  </small>
                  {node.dependsOn.length > 0 && (
                    <small>{t('authoring.flowFrom', { names: node.dependsOn.join(', ') })}</small>
                  )}
                </button>
              );
            })}
          <ActionButton
            label={t('authoring.flowAdd', { kind: kindLabel[column.add].toLowerCase() })}
            variant="quiet"
            onClick={() => onAdd(column.add)}
          />
        </div>
      ))}
    </div>
  );
}

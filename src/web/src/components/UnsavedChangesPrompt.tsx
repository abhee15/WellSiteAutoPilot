import { t } from '../localization';
import { confirmLeave, stayHere, useNavigationQuestion } from '../navigationGuard';
import { ActionButton } from './ActionButton';
import { ConfirmPanel, FormActions } from './FormFields';

/** The question behind the unsaved-changes guard. In-page, like every other confirmation; Stay is first, Leave last. */
export function UnsavedChangesPrompt() {
  if (!useNavigationQuestion()) return null;
  return <ConfirmPanel title={t('unsaved.title')} level={3} focusOnOpen>
    <p>{t('unsaved.message')}</p>
    <FormActions>
      <ActionButton label={t('unsaved.stay')} onClick={stayHere} />
      <ActionButton label={t('unsaved.leave')} onClick={confirmLeave} />
    </FormActions>
  </ConfirmPanel>;
}

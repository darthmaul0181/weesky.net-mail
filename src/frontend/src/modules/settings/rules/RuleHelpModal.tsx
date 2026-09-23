import { Trans, useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'

// The badge is an inline element inside one description, so it travels as a component too.
const HELP_TAGS = { code: <code />, em: <em />, badge: <span className="rule-help-badge" /> }

function Badge() {
  const { t } = useTranslation('settings')
  return <span className="rule-help-badge">{t('rules.help.badge')}</span>
}

export function RuleHelpModal({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('settings')
  // Built from the keys the dropdowns themselves read: restating the wording here is how the
  // help dialog ends up contradicting the control it documents.
  const pair = (a: string, b: string) => t('rules.help.termPair', { a, b })

  return (
    <Modal title={t('rules.help.title')} onClose={onClose} className="rule-help-modal">
      <div className="rule-help-body">

        <section className="rule-help-section">
          <h3 className="rule-help-heading">{t('rules.stepConditions')}</h3>
          <dl className="rule-help-dl">
            <dt>{t('rules.help.headerTerms')}</dt>
            <dd><Trans i18nKey="rules.help.headerDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.fields.Header')}</dt>
            <dd><Trans i18nKey="rules.help.customHeaderDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.help.sizeTerm')}</dt>
            <dd><Trans i18nKey="rules.help.sizeDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.fields.Body')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.bodyDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.fields.EnvelopeFrom'), t('rules.fields.EnvelopeTo'))} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.envelopeDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.fields.RecipientDetail')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.detailDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.help.duplicateTerm')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.duplicateDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.fields.CurrentDate'), t('rules.fields.MessageDate'))} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.dateDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.fields.CurrentWeekday'), t('rules.fields.CurrentHour'))} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.whenDesc" ns="settings" components={HELP_TAGS} /></dd>
          </dl>
        </section>

        <section className="rule-help-section">
          <h3 className="rule-help-heading">{t('rules.help.operators')}</h3>
          <dl className="rule-help-dl">
            <dt>{t('rules.operators.Contains')}</dt>
            <dd><Trans i18nKey="rules.help.containsDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.operators.Equals')}</dt>
            <dd><Trans i18nKey="rules.help.equalsDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.operators.Matches')}</dt>
            <dd><Trans i18nKey="rules.help.wildcardDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.operators.Regex')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.regexDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.operators.Larger'), t('rules.operators.Smaller'))}</dt>
            <dd><Trans i18nKey="rules.help.sizeCompareDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.operators.Before'), t('rules.operators.OnOrAfter'))}</dt>
            <dd><Trans i18nKey="rules.help.dateCompareDesc" ns="settings" components={HELP_TAGS} /></dd>
          </dl>
        </section>

        <section className="rule-help-section">
          <h3 className="rule-help-heading">{t('rules.stepActions')}</h3>
          <dl className="rule-help-dl">
            <dt>{t('rules.actionTypes.FileInto')}</dt>
            <dd><Trans i18nKey="rules.help.fileIntoDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.actionTypes.Redirect')}</dt>
            <dd><Trans i18nKey="rules.help.redirectDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.actionTypes.Reject')}</dt>
            <dd><Trans i18nKey="rules.help.rejectDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.actionTypes.Discard')}</dt>
            <dd><Trans i18nKey="rules.help.discardDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.actionTypes.Keep')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.keepDesc" ns="settings" components={HELP_TAGS} /></dd>
          </dl>
        </section>

        <section className="rule-help-section">
          <h3 className="rule-help-heading">{t('rules.stepOptions')}</h3>
          <dl className="rule-help-dl">
            <dt>{t('rules.markAsRead')}</dt>
            <dd><Trans i18nKey="rules.help.markReadDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.markAsFlagged')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.markFlaggedDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.stopAfter')}</dt>
            <dd><Trans i18nKey="rules.help.stopAfterDesc" ns="settings" components={HELP_TAGS} /></dd>
          </dl>
        </section>

      </div>
    </Modal>
  )
}

import { useTranslation } from 'react-i18next';
import { Users, Receipt, HandCoins, FileUp, PencilLine } from 'lucide-react';
import type { PayableOrigin } from '../../api';

export type PayableIntakeMode = 'fiscal' | 'advance';

/**
 * Intake state at submit time → origin sent on create. Adiantamento = Advance; a parsed NF marks
 * FiscalDocument; the manual sub-path of the fiscal modality (NF not available yet) stays Manual.
 * 'choice' is unreachable at submit (the form only renders inside a modality) — maps to Manual defensively.
 */
export const originForIntake = (mode: 'choice' | PayableIntakeMode, xmlApplied: boolean): PayableOrigin =>
  mode === 'advance' ? 'Advance' : xmlApplied ? 'FiscalDocument' : 'Manual';

/* ============ INTAKE CHOICE (create flow) ============ */
/**
 * First screen of "Nova conta": pick the intake modality. Folha de pagamento is a disabled
 * placeholder (coming soon, origin Payroll reserved); Nota Fiscal nests today's reader + the
 * "fill manually" sub-path; Adiantamento goes straight to the manual form (no NF, ever).
 */
export function PayableIntakeChoice({ onPick }: { onPick: (mode: PayableIntakeMode) => void }) {
  const { t } = useTranslation();
  return (
    <div>
      <div className='intake-choice-title'>{t('payables.intake.title')}</div>
      <div className='intake-choice'>
        <button type='button' className='intake-card' disabled>
          <Users />
          <span className='intake-card-name'>{t('payables.intake.payroll')}</span>
          <span className='intake-card-hint'>{t('payables.intake.payrollHint')}</span>
          <span className='intake-soon-badge'>{t('common.soon')}</span>
        </button>
        <button type='button' className='intake-card is-primary' onClick={() => onPick('fiscal')}>
          <Receipt />
          <span className='intake-card-name'>{t('payables.intake.fiscal')}</span>
          <span className='intake-card-hint'>{t('payables.intake.fiscalHint')}</span>
        </button>
        <button type='button' className='intake-card' onClick={() => onPick('advance')}>
          <HandCoins />
          <span className='intake-card-name'>{t('payables.intake.advance')}</span>
          <span className='intake-card-hint'>{t('payables.intake.advanceHint')}</span>
        </button>
      </div>
    </div>
  );
}

/* ============ FISCAL SUB-CHOICE (fiscal modality) ============ */
/**
 * Second screen of the Nota Fiscal modality: two symmetric blocks. "Enviar arquivo" is itself a file
 * input — clicking it opens the picker directly (no extra screen) and the chosen XML/PDF is parsed +
 * prefilled by the reader. "Preencher manualmente" fills the payable in by hand (NF not available yet).
 */
export function FiscalIntakeChoice({ onUpload, onManual }: { onUpload: (file: File) => void; onManual: () => void }) {
  const { t } = useTranslation();
  return (
    <div>
      <div className='intake-choice-title'>{t('payables.fiscalEntry.title')}</div>
      <div className='intake-choice cols-2'>
        <label className='intake-card is-primary'>
          <FileUp />
          <span className='intake-card-name'>{t('payables.fiscalEntry.upload')}</span>
          <span className='intake-card-hint'>{t('payables.fiscalEntry.uploadHint')}</span>
          <input
            type='file'
            accept='.xml,application/xml,text/xml,.pdf,application/pdf'
            style={{ display: 'none' }}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) onUpload(f);
              // Reset so re-selecting the same file fires onChange again.
              e.target.value = '';
            }}
          />
        </label>
        <button type='button' className='intake-card' onClick={onManual}>
          <PencilLine />
          <span className='intake-card-name'>{t('payables.fiscalEntry.manual')}</span>
          <span className='intake-card-hint'>{t('payables.fiscalEntry.manualHint')}</span>
        </button>
      </div>
    </div>
  );
}

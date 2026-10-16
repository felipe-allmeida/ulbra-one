import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import { useSetPayableDocumentNumber } from '../../queries';
import { Button, Field, Input, ReadonlyField } from '../../ui';
import { dash } from '../../lib';
import { useToast } from '../../toast';
import { getErrorMessage } from '../../apiError';

/**
 * Número do documento (número da NF) da Payable — editável pelo Contábil na Revisão de Contabilidade.
 * Contas vindas de requisição costumam chegar sem o número (só é extraído automaticamente de NF-e XML no
 * aprovação), então quando vazio mostramos um aviso evidente pedindo para o Contábil preencher — o documento
 * está anexado ao lado para consulta. Salvar chama o endpoint que atualiza a própria Payable.
 */
export function DocumentNumberField({
  payableId,
  value,
  editable,
}: {
  payableId: string;
  value: string | null | undefined;
  editable: boolean;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const save = useSetPayableDocumentNumber();
  const current = (value ?? '').trim();
  const [draft, setDraft] = useState(value ?? '');
  useEffect(() => setDraft(value ?? ''), [value]);

  const label = t('payables.classify.fields.documentNumber');
  if (!editable) return <ReadonlyField label={label} value={dash(value)} />;

  const trimmed = draft.trim();
  const dirty = trimmed !== current;

  return (
    <div>
      {current === '' && (
        <div className='form-notice warn' style={{ alignItems: 'flex-start', marginBottom: 10 }}>
          <AlertTriangle size={16} style={{ flexShrink: 0, marginTop: 1 }} />
          <span>{t('payables.documentNumber.emptyWarn')}</span>
        </div>
      )}
      <Field label={label}>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <Input
            value={draft}
            placeholder={t('payables.placeholders.documentNumber')}
            onChange={(e) => setDraft(e.target.value)}
          />
          <Button
            variant='brand'
            disabled={!dirty || save.isPending}
            onClick={() =>
              save.mutate(
                { id: payableId, documentNumber: trimmed || null },
                {
                  onSuccess: () => toast.success(t('payables.documentNumber.saved')),
                  onError: (e) => toast.error(getErrorMessage(e)),
                },
              )
            }
          >
            {t('payables.documentNumber.save')}
          </Button>
        </div>
      </Field>
    </div>
  );
}

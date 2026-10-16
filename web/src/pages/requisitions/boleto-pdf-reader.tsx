import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Paperclip, ScanLine } from 'lucide-react';
import { type BoletoBlobRef, type ParsedBoleto } from '../../api';
import { useParseBoleto, useRequestBoletoUploadUrl, uploadToPresignedUrl } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { FilePicker } from '../../ui';
import { isConvenioBoleto } from './boleto';

const ACCEPT = '.pdf,application/pdf,.png,.jpg,.jpeg,.webp';

/**
 * Per-installment boleto reader (AEL-938): uploads + AI-parses a boleto, applies the fields via `onApply` when a
 * line was read, and hands the already-uploaded blob (key + metadata) to `onBoleto` so the parent attaches it to
 * the requisition by key — the SAME blob, no second upload (mirrors the NF flow). Mirrors FiscalXmlReader's
 * presigned flow (mutateAsync+await for StrictMode); server flags are advisory.
 */
export function BoletoPdfReader({
  companyId,
  onApply,
  onBoleto,
}: {
  companyId: string;
  onApply: (p: ParsedBoleto) => void;
  /** Called with the uploaded blob (key + metadata) so the parent registers it as an attachment — no re-upload. */
  onBoleto?: (blob: BoletoBlobRef) => void;
}) {
  const { t } = useTranslation();
  const requestUrl = useRequestBoletoUploadUrl();
  const parse = useParseBoleto();
  const [parsed, setParsed] = useState<ParsedBoleto | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const onSelect = (f: File | null) => {
    setParsed(null);
    requestUrl.reset();
    parse.reset();
    setErr(null);
    if (!f) return;
    setBusy(true);
    void (async () => {
      try {
        const target = await requestUrl.mutateAsync({ companyId, fileName: f.name });
        await uploadToPresignedUrl(target.uploadUrl, f);
        const p = await parse.mutateAsync({ companyId, key: target.key, fileName: f.name });
        setParsed(p);
        // Reuse THIS blob as the attachment (by key) — no second upload; runs regardless of the parse result.
        // f.type can be '' for some picks; fall back so the by-key register never 400s on empty content type.
        onBoleto?.({
          key: target.key,
          fileName: f.name,
          contentType: f.type || 'application/octet-stream',
          sizeBytes: f.size,
        });
        if (p.digitableLine) onApply(p); // only apply the fields when the AI actually read a line
      } catch (e) {
        setErr(getErrorMessage(e));
      } finally {
        setBusy(false);
      }
    })();
  };

  return (
    <div
      className='boleto-reader'
      style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, marginTop: 6 }}
    >
      <FilePicker accept={ACCEPT} placeholder={t('requisitions.boletoReader.selectHint')} onSelect={onSelect} />
      {busy && (
        <span className='fc-hint' style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
          <ScanLine size={13} /> {t('requisitions.boletoReader.reading')}
        </span>
      )}
      {!busy && err && <span className='field-err'>{err}</span>}
      {!busy && parsed && !parsed.digitableLine && (
        <span className='fc-hint'>{t('requisitions.boletoReader.notRead')}</span>
      )}
      {!busy && parsed?.digitableLine && (
        <span className='fc-hint' style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          {t('requisitions.boletoReader.read')}
          {isConvenioBoleto(parsed.digitableLine) && (
            <span className='fc-boleto-badge'>{t('requisitions.financialComplement.convenioBadge')}</span>
          )}
          {!parsed.lengthValid ? (
            <span className='field-err'>{t('requisitions.boletoReader.lengthWarn')}</span>
          ) : (
            !parsed.checkDigitsOk && (
              <span style={{ color: '#b45309', fontWeight: 500 }}>{t('requisitions.boletoReader.dvWarn')}</span>
            )
          )}
        </span>
      )}
      {/* Attachment note only when the parent attaches the file (create/complement pass onBoleto; recurrence does not). */}
      {!busy && parsed && onBoleto && (
        <span className='fc-hint' style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
          <Paperclip size={12} /> {t('requisitions.boletoReader.attached')}
        </span>
      )}
    </div>
  );
}

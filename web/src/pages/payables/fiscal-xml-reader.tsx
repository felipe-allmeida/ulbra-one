import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Receipt } from 'lucide-react';
import { type ParsedFiscalDocument } from '../../api';
import { useParseFiscalDocument, useRequestFiscalUploadUrl, uploadToPresignedUrl } from '../../queries';
import { brl } from '../../lib';
import { getErrorMessage } from '../../apiError';
import { Button, Input, Field, FilePicker } from '../../ui';
import { fmtDate } from '../../datetime';
import { fmtDoc } from '../../format';

/* ============ FISCAL XML/PDF READER (right column) ============ */
/**
 * Stateless NF reader: upload an XML (NF-e/NFS-e) or PDF; the backend detects + parses it and the fields
 * are applied to the form automatically. Nothing is persisted until the payable is saved.
 */
export function FiscalXmlReader({
  companyId,
  file,
  onFile,
  onApply,
  onUploadKey,
  onClear,
  variant,
  autoFile,
}: {
  companyId: string;
  file: File | null;
  onFile: (f: File | null) => void;
  onApply: (p: ParsedFiscalDocument) => void;
  /** Called once the file is uploaded to blob storage and a key is available, so the parent can attach it on save. */
  onUploadKey: (key: string, meta: { fileName: string; contentType: string; sizeBytes: number }) => void;
  onClear: () => void;
  variant: 'hero' | 'band';
  /** File chosen upstream (the "Enviar arquivo" intake block); parsed automatically, no extra screen. */
  autoFile?: File | null;
}) {
  const { t } = useTranslation();
  const requestUrl = useRequestFiscalUploadUrl();
  const parse = useParseFiscalDocument();
  const [parsed, setParsed] = useState<ParsedFiscalDocument | null>(null);
  const [uploadErr, setUploadErr] = useState<string | null>(null);
  const hero = variant === 'hero';

  // Selecting a file: request a presigned upload URL → PUT the file to blob storage →
  // parse it by key → apply. The file is uploaded once and the same key is reused on save.
  const onSelect = (f: File | null) => {
    setParsed(null);
    parse.reset();
    requestUrl.reset();
    setUploadErr(null);
    onClear();
    onFile(f);
    if (!f) return;
    requestUrl.mutate(
      { fileName: f.name },
      {
        onSuccess: async (target) => {
          try {
            await uploadToPresignedUrl(target.uploadUrl, f);
          } catch (e) {
            setUploadErr(e instanceof Error ? e.message : String(e));
            return;
          }
          onUploadKey(target.key, { fileName: f.name, contentType: f.type, sizeBytes: f.size });
          parse.mutate(
            { companyId, key: target.key, fileName: f.name },
            {
              onSuccess: (p) => {
                setParsed(p);
                onApply(p);
              },
            },
          );
        },
      },
    );
  };

  // A file picked upstream ("Enviar arquivo") is processed once, running the same upload+parse flow as
  // the inline picker — so the user never sees a separate upload screen.
  const autoProcessedRef = useRef<File | null>(null);
  useEffect(() => {
    if (autoFile && autoProcessedRef.current !== autoFile) {
      autoProcessedRef.current = autoFile;
      onSelect(autoFile);
    }
    // onSelect is recreated each render on purpose; we key the run on the autoFile identity only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoFile]);

  const clearAll = () => {
    setParsed(null);
    parse.reset();
    requestUrl.reset();
    setUploadErr(null);
    onFile(null);
    onClear();
  };

  return (
    <div
      className={hero ? 'card-form fiscal-hero' : 'card-form fiscal-band'}
      // Hero keeps the centered 560px cap (.fiscal-hero); the band spans the full form width.
      style={hero ? undefined : { maxWidth: 'none' }}
    >
      <div className='section-title' style={{ marginTop: 0, display: 'flex', alignItems: 'center', gap: 8 }}>
        {!hero && <Receipt style={{ width: 16, height: 16, color: 'var(--brand)' }} />}
        {hero ? t('payables.fiscalReader.title') : t('payables.fiscalReader.bandTitle')}
        {!hero && parsed && <span className='tag-readonly'>{t('payables.fiscalReader.readOnlyBadge')}</span>}
      </div>
      {hero && <p className='fiscal-hero-emphasis'>{t('payables.fiscalReader.heroEmphasis')}</p>}
      <Field label={t('payables.fiscalReader.selectLabel')} help={t('payables.fiscalReader.selectHelp')}>
        <FilePicker
          accept='.xml,application/xml,text/xml,.pdf,application/pdf'
          fileName={file?.name}
          placeholder={hero ? t('payables.fiscalReader.dropHint') : t('common.noFileSelected')}
          onSelect={onSelect}
          large={hero}
        />
      </Field>

      {(requestUrl.isPending || parse.isPending) && (
        <div className='help' style={{ marginTop: 10 }}>
          {t('payables.fiscalReader.parsing')}
        </div>
      )}
      {(uploadErr != null || requestUrl.error != null || parse.error != null) && (
        <div className='create-err' style={{ marginTop: 10 }}>
          {uploadErr ?? getErrorMessage(requestUrl.error ?? parse.error)}
        </div>
      )}

      {parsed && (
        <>
          <div className='help' style={{ margin: '12px 0 8px' }}>
            {t('payables.fiscalReader.applied')}
          </div>
          <div className='edit-grid'>
            <FiscalField
              label={t('payables.fiscalReader.format')}
              value={t(`payables.fiscalReader.formats.${parsed.format}`)}
            />
            <FiscalField label={t('payables.fields.documentNumber')} value={parsed.documentNumber ?? '—'} />
            <FiscalField
              label={t('payables.fields.issueDate')}
              value={parsed.issueDate ? fmtDate(parsed.issueDate.slice(0, 10)) : '—'}
            />
            {parsed.rpsNumber && <FiscalField label={t('payables.fiscalReader.rpsNumber')} value={parsed.rpsNumber} />}
            {parsed.rpsSeries && <FiscalField label={t('payables.fiscalReader.rpsSeries')} value={parsed.rpsSeries} />}
            <FiscalField
              label={t('payables.fields.totalAmount')}
              value={parsed.totalAmount != null ? brl(parsed.totalAmount) : '—'}
            />
            {parsed.cnaeCode && <FiscalField label={t('payables.fiscalReader.cnae')} value={parsed.cnaeCode} />}
            {parsed.installments.length > 0 && (
              <FiscalField
                label={t('payables.fiscalReader.installmentsCount')}
                value={String(parsed.installments.length)}
              />
            )}
            {/* Emitente e CNPJ do emitente lado a lado (linha própria de 2 colunas). */}
            <div style={{ gridColumn: '1 / -1', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px 16px' }}>
              <FiscalField label={t('payables.fiscalReader.issuer')} value={parsed.issuerName ?? '—'} />
              <FiscalField
                label={t('payables.fiscalReader.issuerDocument')}
                value={parsed.issuerDocument ? fmtDoc(parsed.issuerDocument) : '—'}
              />
            </div>
            <FiscalField
              full
              label={t('payables.fiscalReader.vendorMatch')}
              value={parsed.matchedVendorName ?? t('payables.fiscalReader.vendorNoMatch')}
            />
            {parsed.serviceDescription && (
              <FiscalField
                full
                label={t(
                  parsed.format === 'NfeXml'
                    ? 'payables.fiscalReader.productDescription'
                    : 'payables.fiscalReader.serviceDescription',
                )}
                value={parsed.serviceDescription}
              />
            )}
          </div>
          <Button
            type='button'
            onClick={clearAll}
            style={{ marginTop: 14, background: 'var(--surface)', color: 'var(--muted-foreground)' }}
          >
            {t('payables.fiscalReader.clear')}
          </Button>
        </>
      )}
    </div>
  );
}

/** Read-only field for the NF preview — same Label + input.input look as the form. */
function FiscalField({ label, value, full }: { label: string; value: string; full?: boolean }) {
  return (
    <div style={full ? { gridColumn: '1 / -1' } : undefined}>
      <Field label={label}>
        <Input value={value} readOnly />
      </Field>
    </div>
  );
}

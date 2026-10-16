import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle, Download, FileText } from 'lucide-react';
import {
  api,
  type RemittanceCriticism,
  type RemittanceReturnHistoryItem,
  type RemittanceReturnPreview,
  type RemittanceReturnPreviewEntry,
} from '../../api';
import {
  useBankAccounts,
  useConfirmRemittanceLine,
  useMarkRemittanceLineReturned,
  useRemittanceReturnAutoConfirm,
  useRemittanceReturnCriticism,
  useRemittanceReturnHistory,
  useRemittanceReturnPreview,
} from '../../queries';
import { getErrorMessage } from '../../apiError';
import { downloadWithAuth, fileErrorKey } from '../../download';
import { useToast } from '../../toast';
import { Button, cx, EmptyState, FilePicker, Panel, Select, ToolbarField } from '../../ui';
import { useOriginNav } from '../../nav';
import { P, usePermissions } from '../../permissions';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { usePhone } from '../../useMediaQuery';

const brl = (v: number | null | undefined) => (v ?? 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

/** Date + time — two returns for the same account often land on the same day. */
const dmyhm = (iso: string) =>
  new Date(iso).toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });

const dmy = (iso: string | null) => {
  if (!iso) return '—';
  const [y, m, d] = iso.split('-');
  return y && m && d ? `${d}/${m}/${y}` : iso;
};

/**
 * Badge por bucket de classificação — as mesmas classes do resto do app (`.badge-status`), não uma
 * paleta paralela. As cores hex que viviam aqui ignoravam os tokens de tema e quebravam no escuro.
 * `badge-neutral` em Unmatched é deliberado: nada é pedido ao operador, então nem quente nem calmo.
 */
const CLASS_BADGE: Record<string, string> = {
  Paid: 'badge-pago',
  Rejected: 'badge-rejeitado',
  Divergent: 'badge-pendente',
  Ambiguous: 'badge-pendente',
  Unmatched: 'badge-neutral',
  AlreadyProcessed: 'badge-aprovado',
};

type SubTab = 'reconciliation' | 'criticism';

/**
 * "Retorno CNAB" tab of Gestão Financeira. Two sub-tabs mirroring the ORCAMENTO legado:
 * - Conciliação de Retorno (MOVIMENTO): upload → auto-baixa the paid lines, then confirm/return the rest.
 * - Análise de Crítica (CRÍTICA): upload the bank's validation file → report accepted/rejected, NO baixa;
 *   the operator removes rejected titles from the batch (releases the installment back to the queue).
 */
export function CnabReturnTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const accounts = useBankAccounts(companyId).data ?? [];
  const [bankAccountId, setBankAccountId] = useState('');
  const [subTab, setSubTab] = useState<SubTab>('reconciliation');

  useEffect(() => {
    setBankAccountId((cur) => (accounts.find((a) => a.id === cur) ? cur : (accounts[0]?.id ?? '')));
  }, [accounts]);

  return (
    <div style={{ marginTop: 12 }}>
      <div className='toolbar' style={{ marginBottom: 12 }}>
        <ToolbarField label={t('remittances.return.cnabTab.payingAccount')}>
          <Select value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)}>
            <option value=''>{t('remittances.return.cnabTab.payingAccountSelect')}</option>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.alias}
              </option>
            ))}
          </Select>
        </ToolbarField>
      </div>

      <div className='seg' style={{ marginBottom: 12 }}>
        {(['reconciliation', 'criticism'] as SubTab[]).map((tab) => (
          <button
            key={tab}
            type='button'
            className={cx('segbtn', subTab === tab && 'on')}
            onClick={() => setSubTab(tab)}
          >
            {t(`remittances.return.cnabTab.subTab.${tab}`)}
          </button>
        ))}
      </div>

      {!bankAccountId ? (
        <EmptyState
          icon={<FileText />}
          title={t('remittances.return.cnabTab.noAccountTitle')}
          sub={t('remittances.return.cnabTab.noAccountSub')}
        />
      ) : subTab === 'reconciliation' ? (
        // key resets the sub-tab's file/report state when the paying account changes.
        <ReconciliationSubTab key={bankAccountId} bankAccountId={bankAccountId} />
      ) : (
        <CriticismSubTab key={bankAccountId} bankAccountId={bankAccountId} />
      )}
    </div>
  );
}

/** MOVIMENTO: upload auto-baixa + manual confirm/return per line. */
function ReconciliationSubTab({ bankAccountId }: { bankAccountId: string }) {
  const { t } = useTranslation();
  const toast = useToast();
  const openPayable = useOriginNav();
  const canWrite = usePermissions().can(P.treasuryWrite);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<RemittanceReturnPreview | null>(null);

  // Dry-run preview — used only to REFRESH the view after a manual per-line action (no re-baixa).
  const previewMut = useRemittanceReturnPreview({
    onSuccess: (data) => setPreview(data),
    onError: (e) => toast.error(getErrorMessage(e)),
  });
  // Upload — auto-confirms the "Pago" lines (baixa) so the operator only handles the rest manually.
  const autoConfirmMut = useRemittanceReturnAutoConfirm({
    onSuccess: (data) => {
      setPreview(data);
      if (data.autoConfirmed > 0)
        toast.success(t('remittances.return.cnabTab.autoConfirmed', { count: data.autoConfirmed }));
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const runUpload = (f: File | null) => {
    if (!f || !bankAccountId) return;
    setFile(f);
    autoConfirmMut.mutate({ payingBankAccountId: bankAccountId, file: f });
  };
  // After a per-line action, re-run the (dry-run) preview so the row reflects its new state.
  const refresh = () => {
    if (file && bankAccountId) previewMut.mutate({ payingBankAccountId: bankAccountId, file });
  };

  const confirmPaid = useConfirmRemittanceLine({
    onSuccess: () => {
      toast.success(t('remittances.return.confirmPaidSuccess'));
      refresh();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });
  const markReturned = useMarkRemittanceLineReturned({
    onSuccess: () => {
      toast.success(t('remittances.return.returnLineSuccess'));
      refresh();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const busy = confirmPaid.isPending || markReturned.isPending || previewMut.isPending || autoConfirmMut.isPending;
  const actionsBlocked = !canWrite || !preview?.trailerOk || busy;

  const onConfirm = (e: RemittanceReturnPreviewEntry) => {
    if (!e.remittanceId || !e.lineId) return;
    confirmPaid.mutate({
      id: e.remittanceId,
      lineId: e.lineId,
      ret: { effectiveDate: e.effectiveDate, bankReference: e.bankReference, authCode: e.authCode },
    });
  };
  const onReturn = (e: RemittanceReturnPreviewEntry) => {
    if (!e.remittanceId || !e.lineId) return;
    markReturned.mutate({
      id: e.remittanceId,
      lineId: e.lineId,
      reason: e.motivo ?? e.note ?? t('remittances.return.cnabTab.defaultReturnReason'),
    });
  };

  const c = preview?.counters;

  return (
    <>
      {canWrite && (
        <div className='toolbar' style={{ marginBottom: 12 }}>
          <ToolbarField label={t('remittances.return.cnabTab.fileLabel')} className='cnab-return-file'>
            <FilePicker
              accept='.ret,.RET,.txt,.TXT,.csv,.CSV,text/plain,text/csv'
              placeholder={
                autoConfirmMut.isPending ? t('remittances.loading') : t('remittances.return.cnabTab.uploadPlaceholder')
              }
              fileName={file?.name}
              onSelect={runUpload}
            />
          </ToolbarField>
        </div>
      )}

      {!preview ? (
        <EmptyState
          icon={<FileText />}
          title={t('remittances.return.cnabTab.emptyTitle')}
          sub={t('remittances.return.cnabTab.emptySub')}
        />
      ) : (
        <>
          {!preview.trailerOk && (
            <div
              className='form-notice warn'
              style={{ marginBottom: 8, display: 'flex', gap: 8, alignItems: 'center' }}
            >
              <AlertTriangle size={16} />
              <span>
                <strong>{t('remittances.return.cnabTab.trailerErrorTitle')}</strong> {preview.trailerMessages.join(' ')}
              </span>
            </div>
          )}
          {preview.payer && (
            <div className='form-notice' style={{ marginBottom: 8 }}>
              {t('remittances.return.cnabTab.payerLine', {
                document: preview.payer.document,
                agency: preview.payer.agency,
                account: preview.payer.account,
              })}
            </div>
          )}

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 12 }}>
            <Tile label={t('remittances.return.cnabTab.tiles.paid')} value={c?.paid} tone='var(--success)' />
            <Tile label={t('remittances.return.cnabTab.tiles.rejected')} value={c?.rejected} tone='var(--danger)' />
            <Tile label={t('remittances.return.cnabTab.tiles.divergent')} value={c?.divergent} tone='var(--warning)' />
            <Tile label={t('remittances.return.cnabTab.tiles.ambiguous')} value={c?.ambiguous} tone='var(--warning)' />
            <Tile
              label={t('remittances.return.cnabTab.tiles.unmatched')}
              value={c?.unmatched}
              tone='var(--muted-foreground)'
            />
          </div>

          <Panel title={t('remittances.return.cnabTab.entriesTitle', { total: c?.total ?? 0 })}>
            <ReconciliationEntriesTable
              entries={preview.entries}
              actionsBlocked={actionsBlocked}
              onConfirm={onConfirm}
              onReturn={onReturn}
              onOpenPayable={openPayable}
            />
          </Panel>
        </>
      )}

      <ReturnHistoryPanel bankAccountId={bankAccountId} />
    </>
  );
}

/**
 * Ordem = a ordem já renderizada hoje (título/favorecido, banco/efetivado, classificação, ações).
 * `title` bundla favorecido+referência+nominal (mesmo padrão de conteúdo composto do cnab-return-tab
 * inteiro); `bank` (valor efetivado+data+motivo+authCode) não é um "amount" limpo — é um bloco
 * informativo com o valor só como primeira linha — por isso `detail`, não um segundo `amount`
 * (ver aviso de cardinalidade em ResponsiveTable.tsx). `onRowClick` abre o Payable casado, e
 * `rowClickable` limita a afordância de clique às linhas que têm um (`e.payableId`).
 *
 * `refAndNominal` tinha sido emendado (telefone-só) dentro da célula `detail` de `bank` — no card
 * expandido isso rotularia referência/documento/valor nominal como "Banco (efetivado)", um rótulo
 * que descreve o valor efetivado do banco, não a referência do sistema (mesmo defeito já rejeitado
 * neste projeto para o rodapé de totais e corrigido em `users.tsx`/commit 4e8a4240). Ganha coluna
 * `detail` própria, só no telefone, com `header` próprio.
 */
function ReconciliationEntriesTable({
  entries,
  actionsBlocked,
  onConfirm,
  onReturn,
  onOpenPayable,
}: {
  entries: RemittanceReturnPreviewEntry[];
  actionsBlocked: boolean;
  onConfirm: (e: RemittanceReturnPreviewEntry) => void;
  onReturn: (e: RemittanceReturnPreviewEntry) => void;
  /** A linha tem a anatomia das outras tabelas do app (favorecido, valor, ref), então clicar nela
   * abre o título — quando existe um. Sem casamento não há para onde ir; ver a nota sobre `onRowClick`
   * uniforme do primitivo logo abaixo. */
  onOpenPayable: (path: string) => void;
}) {
  const { t } = useTranslation();
  // `.rt-title` forces one-line nowrap+ellipsis (ResponsiveTable.tsx) — inherited by nested blocks,
  // so the reference/nominal lines below the name would clip with no "…" on phone (checked at
  // 390px). They move into the `bank` detail cell there instead; desktop keeps `title` untouched.
  const phone = usePhone();
  const refAndNominal = (e: RemittanceReturnPreviewEntry) => (
    <>
      <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
        {t('remittances.return.cnabTab.refLabel')}: {e.reference}
        {e.favoredDocument ? ` · ${e.favoredDocument}` : ''}
      </div>
      <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
        {t('remittances.return.cnabTab.nominal')}: {brl(e.valorNominal)}
      </div>
    </>
  );
  const columns: Column<RemittanceReturnPreviewEntry>[] = [
    {
      key: 'title',
      header: t('remittances.return.cnabTab.col.title'),
      role: 'title',
      cell: (e) => (
        <>
          <div style={{ fontWeight: 600 }}>{e.favoredName ?? '—'}</div>
          {!phone && refAndNominal(e)}
        </>
      ),
    },
    {
      key: 'bank',
      header: t('remittances.return.cnabTab.col.bank'),
      role: 'detail',
      cell: (e) => (
        <>
          <div style={{ fontWeight: 600 }}>{brl(e.valorEfetivado)}</div>
          <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>{dmy(e.effectiveDate)}</div>
          <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
            {e.motivo ?? e.occurrenceCodes.join(', ')}
          </div>
          {e.authCode && (
            <div style={{ fontSize: 10, color: 'var(--muted-foreground)', fontFamily: 'monospace' }}>{e.authCode}</div>
          )}
        </>
      ),
    },
    // Só existe no telefone — no desktop a referência/documento/valor nominal já mora dentro de
    // `title` (ver acima), e esta coluna nunca é adicionada ao array, então a contagem de colunas
    // do desktop não muda. Tem `header` próprio para que o painel expandido rotule o valor como
    // "Referência / Valor nominal", não como "Banco (efetivado)" (ver comentário acima do array).
    ...(phone
      ? [
          {
            key: 'reference',
            header: t('remittances.return.cnabTab.col.reference'),
            role: 'detail' as const,
            cell: (e: RemittanceReturnPreviewEntry) => refAndNominal(e),
          },
        ]
      : []),
    {
      key: 'classification',
      header: t('remittances.return.cnabTab.col.classification'),
      role: 'status',
      // Badge por bucket de classificação via `CLASS_BADGE` — as mesmas classes do resto do app
      // (`.badge-status`), não uma paleta paralela em hex que ignora os tokens de tema.
      cell: (e) => (
        <>
          <span className={`badge-status ${CLASS_BADGE[e.classification] ?? CLASS_BADGE.Unmatched}`}>
            {t(`remittances.return.cnabTab.class.${e.classification}`)}
          </span>
          <div style={{ fontSize: 10, color: 'var(--muted-foreground)', marginTop: 2 }}>
            {t(`remittances.return.cnabTab.method.${e.matchMethod}`)}
          </div>
        </>
      ),
    },
    {
      key: 'actions',
      header: t('remittances.return.cnabTab.col.actions'),
      role: 'detail',
      align: 'right',
      cell: (e) => {
        const matched = !!e.remittanceId && !!e.lineId;
        const actionable = matched && e.classification !== 'AlreadyProcessed';
        return (
          <span style={{ whiteSpace: 'nowrap' }}>
            {!matched && <span className='help'>{t('remittances.return.cnabTab.noMatchNote')}</span>}
            {actionable && (
              <>
                <Button variant='brand' onClick={() => onConfirm(e)} disabled={actionsBlocked}>
                  {t('remittances.return.confirmPaid')}
                </Button>{' '}
                <Button variant='danger-outline' onClick={() => onReturn(e)} disabled={actionsBlocked}>
                  {t('remittances.return.returnLine')}
                </Button>
              </>
            )}
          </span>
        );
      },
    },
  ];

  // `reference` sozinho pode repetir entre linhas (mesmo motivo do <tbody> original usar o índice do
  // map na key) — `indexOf` reconstrói o mesmo desempate a partir da própria linha.
  return (
    <ResponsiveTable
      rows={entries}
      columns={columns}
      rowKey={(e) => `${e.reference}-${entries.indexOf(e)}`}
      // Unmatched/Ambiguous não têm título casado: sem `rowClickable` a linha ganharia cursor e
      // hover de clicável sem ter para onde ir.
      rowClickable={(e) => e.payableId != null}
      onRowClick={(e) => e.payableId && onOpenPayable(`/app/payables/${e.payableId}`)}
    />
  );
}

/** CRÍTICA: read the bank's validation file → report accepted/rejected. Never applies baixa. */
function CriticismSubTab({ bankAccountId }: { bankAccountId: string }) {
  const { t } = useTranslation();
  const toast = useToast();
  const canWrite = usePermissions().can(P.treasuryWrite);
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<RemittanceCriticism | null>(null);

  const criticismMut = useRemittanceReturnCriticism({
    onSuccess: (data) => setReport(data),
    onError: (e) => toast.error(getErrorMessage(e)),
  });
  // "Remover do Lote" reuses mark-returned (releases the installment InRemittance → Open).
  const removeFromBatch = useMarkRemittanceLineReturned({
    onSuccess: () => {
      toast.success(t('remittances.return.cnabTab.criticism.removeSuccess'));
      if (file && bankAccountId) criticismMut.mutate({ payingBankAccountId: bankAccountId, file });
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const runUpload = (f: File | null) => {
    if (!f || !bankAccountId) return;
    setFile(f);
    criticismMut.mutate({ payingBankAccountId: bankAccountId, file: f });
  };

  const busy = criticismMut.isPending || removeFromBatch.isPending;

  const onRemove = (rec: RemittanceCriticism['records'][number]) => {
    if (!rec.remittanceId || !rec.lineId) return;
    removeFromBatch.mutate({
      id: rec.remittanceId,
      lineId: rec.lineId,
      reason: t('remittances.return.cnabTab.criticism.removeReason', {
        motivo: rec.motivo ?? rec.occurrenceCodes.join(', '),
      }),
    });
  };

  return (
    <>
      {canWrite && (
        <div className='toolbar' style={{ marginBottom: 12 }}>
          <ToolbarField label={t('remittances.return.cnabTab.criticism.fileLabel')} className='cnab-return-file'>
            <FilePicker
              accept='.ret,.RET,.txt,.TXT,text/plain'
              placeholder={
                criticismMut.isPending
                  ? t('remittances.loading')
                  : t('remittances.return.cnabTab.criticism.uploadPlaceholder')
              }
              fileName={file?.name}
              onSelect={runUpload}
            />
          </ToolbarField>
        </div>
      )}

      {!report ? (
        <EmptyState
          icon={<FileText />}
          title={t('remittances.return.cnabTab.criticism.emptyTitle')}
          sub={t('remittances.return.cnabTab.criticism.emptySub')}
        />
      ) : (
        <>
          {report.payer && (
            <div className='form-notice' style={{ marginBottom: 8 }}>
              {t('remittances.return.cnabTab.payerLine', {
                document: report.payer.document,
                agency: report.payer.agency,
                account: report.payer.account,
              })}
            </div>
          )}

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 12 }}>
            <Tile
              label={t('remittances.return.cnabTab.criticism.tiles.total')}
              value={report.total}
              tone='var(--muted-foreground)'
            />
            <Tile
              label={t('remittances.return.cnabTab.criticism.tiles.success')}
              value={report.success}
              tone='var(--success)'
            />
            <Tile
              label={t('remittances.return.cnabTab.criticism.tiles.rejection')}
              value={report.rejection}
              tone='var(--danger)'
            />
          </div>

          <Panel title={t('remittances.return.cnabTab.criticism.entriesTitle', { total: report.total })}>
            <CriticismRecordsTable records={report.records} canWrite={canWrite} busy={busy} onRemove={onRemove} />
          </Panel>
        </>
      )}
    </>
  );
}

/** Ordem = record/status/actions, igual ao desktop hoje. `record` bundla favorecido+referência+nominal
 * (title); `status` é o badge aceito/rejeitado + motivo; `actions` remove do lote. Sem
 * `onRowClick` — nunca houve navegação de linha nesta tabela.
 *
 * `refAndNominal` tinha sido emendado (telefone-só) dentro da célula `detail` de `actions` — no card
 * expandido isso rotularia referência/valor nominal como "Ações", um rótulo que não descreve o
 * conteúdo (mesmo defeito já rejeitado neste projeto para o rodapé de totais e corrigido em
 * `users.tsx`/commit 4e8a4240). Ganha coluna `detail` própria, só no telefone, com `header` próprio.
 */
function CriticismRecordsTable({
  records,
  canWrite,
  busy,
  onRemove,
}: {
  records: RemittanceCriticism['records'];
  canWrite: boolean;
  busy: boolean;
  onRemove: (rec: RemittanceCriticism['records'][number]) => void;
}) {
  const { t } = useTranslation();
  // Same `.rt-title` nowrap/clip constraint as ReconciliationEntriesTable above — reference/nominal
  // move into the `actions` detail cell on phone instead of risking a clip under a long name.
  const phone = usePhone();
  const refAndNominal = (rec: RemittanceCriticism['records'][number]) => (
    <>
      <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
        {t('remittances.return.cnabTab.refLabel')}: {rec.reference}
      </div>
      <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
        {t('remittances.return.cnabTab.nominal')}: {brl(rec.amount)}
      </div>
    </>
  );
  const columns: Column<RemittanceCriticism['records'][number]>[] = [
    {
      key: 'record',
      header: t('remittances.return.cnabTab.criticism.col.record'),
      role: 'title',
      cell: (rec) => (
        <>
          <div style={{ fontWeight: 600 }}>{rec.favoredName ?? '—'}</div>
          {!phone && refAndNominal(rec)}
        </>
      ),
    },
    {
      key: 'status',
      header: t('remittances.return.cnabTab.criticism.col.status'),
      role: 'status',
      cell: (rec) => {
        const badge = rec.isSuccess ? 'badge-pago' : 'badge-rejeitado';
        const codes = rec.occurrenceCodes.join(', ');
        return (
          <>
            <span className={`badge-status ${badge}`}>
              {rec.isSuccess
                ? t('remittances.return.cnabTab.criticism.accepted')
                : t('remittances.return.cnabTab.criticism.rejected')}
              {codes ? ` (${codes})` : ''}
            </span>
            {!rec.isSuccess && rec.motivo && (
              <div style={{ fontSize: 11, color: 'var(--danger)', marginTop: 2 }}>{rec.motivo}</div>
            )}
          </>
        );
      },
    },
    // Só existe no telefone — no desktop referência/valor nominal já moram dentro de `record` (ver
    // acima), e esta coluna nunca é adicionada ao array, então a contagem de colunas do desktop não
    // muda. Tem `header` próprio para que o painel expandido rotule o valor como
    // "Referência / Valor", não como "Ações" (ver comentário acima do array).
    ...(phone
      ? [
          {
            key: 'reference',
            header: t('remittances.return.cnabTab.criticism.col.reference'),
            role: 'detail' as const,
            cell: (rec: RemittanceCriticism['records'][number]) => refAndNominal(rec),
          },
        ]
      : []),
    {
      key: 'actions',
      header: t('remittances.return.cnabTab.criticism.col.actions'),
      role: 'detail',
      align: 'right',
      cell: (rec) => {
        const inBatch = !!rec.remittanceId && !!rec.lineId && rec.lineStatus === 'Pending';
        const removed = rec.lineStatus === 'Returned';
        return rec.isSuccess ? null : removed ? (
          <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--muted-foreground)' }}>
            {t('remittances.return.cnabTab.criticism.removed')}
          </span>
        ) : inBatch ? (
          <Button variant='danger-outline' onClick={() => onRemove(rec)} disabled={!canWrite || busy}>
            {t('remittances.return.cnabTab.criticism.removeFromBatch')}
          </Button>
        ) : null;
      },
    },
  ];

  return (
    <ResponsiveTable rows={records} columns={columns} rowKey={(rec) => `${rec.reference}-${records.indexOf(rec)}`} />
  );
}

/**
 * The .RET files already processed for this paying account. The upload stores the bank's own document, and
 * this is where it is read back — so it renders even before anything is uploaded in this session, and a file
 * that applied nothing (divergent trailer) shows up flagged rather than being hidden.
 *
 * A tabela nunca teve `<thead>` — `hideHeader` evita acrescentar um cabeçalho que não existia (ver
 * ResponsiveTable.tsx). `file` bundla nome+selo "não aplicado" (curto, seguro como conteúdo inline de
 * `title`); processedBy+contadores são 2 linhas extras que no desktop continuam na mesma célula de
 * sempre — mas dentro do `.rt-title` do card elas quebrariam o truncamento com "…" (aviso de conteúdo
 * composto em ResponsiveTable.tsx), então só aparecem lá com `!phone`, reaparecendo em `actions` (com
 * `phone &&`) — mesma técnica já usada acima em ReconciliationEntriesTable/CriticismRecordsTable.
 * `actions` usa `header: ''` porque a coluna nunca teve cabeçalho
 * visível (mesmo padrão de cash-flow.tsx/relacao-tab.tsx).
 */
function ReturnHistoryPanel({ bankAccountId }: { bankAccountId: string }) {
  const { t } = useTranslation();
  const [err, setErr] = useState<string | null>(null);
  const items = useRemittanceReturnHistory(bankAccountId).data?.items ?? [];
  const phone = usePhone();

  const secondaryLines = (r: RemittanceReturnHistoryItem) => (
    <>
      <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
        {t('remittances.return.cnabTab.history.processedBy', { date: dmyhm(r.processedAt), name: r.processedBy })}
      </div>
      <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
        {t('remittances.return.cnabTab.history.counters', {
          paid: r.paid,
          returned: r.returned,
          divergent: r.divergent,
          unmatched: r.unmatched,
        })}
      </div>
    </>
  );

  const columns: Column<RemittanceReturnHistoryItem>[] = [
    {
      key: 'file',
      header: t('remittances.return.cnabTab.history.col.file'),
      role: 'title',
      cell: (r) => (
        <>
          <div style={{ fontWeight: 600 }}>
            {r.fileName}
            {!r.applied && (
              <span
                style={{
                  marginLeft: 8,
                  background: 'var(--danger-subtle)',
                  color: 'var(--danger)',
                  padding: '2px 8px',
                  borderRadius: 999,
                  fontSize: 11,
                  fontWeight: 700,
                }}
              >
                {t('remittances.return.cnabTab.history.notApplied')}
              </span>
            )}
          </div>
          {!phone && secondaryLines(r)}
        </>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (r) => (
        <>
          {phone && secondaryLines(r)}
          <span style={{ whiteSpace: 'nowrap' }}>
            <Button
              icon={<Download />}
              onClick={() => {
                setErr(null);
                downloadWithAuth(api.remittanceReturnFileUrl(r.id), r.fileName).catch((e) =>
                  setErr(t(fileErrorKey(e, 'remittances.return.cnabTab.history.downloadError'))),
                );
              }}
            >
              {t('remittances.return.cnabTab.history.download')}
            </Button>
          </span>
        </>
      ),
    },
  ];

  return (
    <div style={{ marginTop: 12 }}>
      <Panel title={t('remittances.return.cnabTab.history.title')}>
        {items.length === 0 ? (
          <div className='empty-state' style={{ padding: 24 }}>
            <div className='s'>{t('remittances.return.cnabTab.history.empty')}</div>
          </div>
        ) : (
          <ResponsiveTable rows={items} columns={columns} rowKey={(r) => r.id} hideHeader />
        )}
        {err && <p className='create-err'>{err}</p>}
      </Panel>
    </div>
  );
}

function Tile({ label, value, tone }: { label: string; value: number | undefined; tone: string }) {
  return (
    <div
      style={{
        minWidth: 96,
        border: '1px solid var(--border)',
        borderRadius: 12,
        padding: '8px 12px',
        background: 'var(--card)',
        textAlign: 'center',
      }}
    >
      <div style={{ fontSize: 20, fontWeight: 800, color: tone, lineHeight: 1 }}>{value ?? 0}</div>
      <div style={{ fontSize: 10, fontWeight: 700, color: 'var(--muted-foreground)', textTransform: 'uppercase' }}>
        {label}
      </div>
    </div>
  );
}

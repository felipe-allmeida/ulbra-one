import { Fragment, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ChevronDown } from 'lucide-react';
import { usePhone } from '../useMediaQuery';
import { cx, onRowClick as makeRowClickHandler } from '../ui';

/**
 * O papel de cada coluna. É o que permite ao primitivo montar o card sozinho:
 *
 *   title   — exatamente 1. A identificação da linha.
 *   amount  — no máximo 1. O número que o olho procura; dois números concorrendo
 *             derrotam o propósito. Colunas monetárias adicionais (líquido, saldo,
 *             desconto) são `detail`.
 *   meta    — 0 a 2. Mais que isso não cabe numa linha de 390px sem truncar tudo.
 *   status  — 0 ou mais. Badges empilham naturalmente.
 *   detail  — 0 ou mais. O padrão seguro: na dúvida, `detail`.
 *
 * No desktop o papel é ignorado: toda coluna vira <th>/<td> na ordem declarada.
 */
export type ColumnRole = 'title' | 'meta' | 'amount' | 'status' | 'detail';

export interface Column<T> {
  key: string;
  /** Normalmente uma string; aceita ReactNode para cabeçalhos com controle próprio (ex.: botão de
   *  ordenação) — no telefone só aparece como rótulo de coluna `detail`, nunca como cabeçalho de
   *  card, então um controle interativo aqui não tem efeito nenhum lá (ver CardList abaixo). */
  header: ReactNode;
  role: ColumnRole;
  cell: (row: T) => ReactNode;
  align?: 'left' | 'center' | 'right';
  /** Conteúdo da linha de totais para esta coluna. Colunas sem `footCell` ficam vazias no rodapé
   *  do desktop e não aparecem no resumo do telefone. `rows` é a lista **visível** (a página/filtro
   *  atual já aplicado) — nunca o conjunto de dados inteiro. Se o total precisa refletir mais do que
   *  as linhas exibidas (uma agregação da API, ou um valor que pode legitimamente divergir da soma
   *  das linhas — ex.: um filtro parcial), leia-o da resposta da API em vez de somar `rows` aqui. */
  footCell?: (rows: T[]) => ReactNode;
}

export interface ResponsiveTableProps<T> {
  rows: T[];
  columns: Column<T>[];
  rowKey: (row: T) => string;
  onRowClick?: (row: T) => void;
  /** Legenda da linha de totais (ex.: "Total", "Totais") — título do rodapé: primeira célula do
   *  `<tfoot>` no desktop, título do card de resumo no telefone. O rodapé é renderizado quando
   *  `footLabel` está definida OU quando qualquer coluna declara `footCell` (os dois são
   *  independentes: uma tabela pode ter só somas sem legenda textual, ou uma legenda sem soma). */
  footLabel?: ReactNode;
  /** Classe extra por linha (ex.: destacar uma linha abaixo da reserva mínima) — aplicada ao `<tr>`
   *  no desktop e ao `.rt-card` no telefone, para o sinal sobreviver nos dois tamanhos. */
  rowClassName?: (row: T) => string | undefined;
  /** Quais linhas de fato navegam, quando só algumas navegam — ex.: uma entrada de retorno CNAB
   *  sem título conciliado não tem para onde ir. Sem ela, toda linha navega quando `onRowClick`
   *  existe. Portão único de propósito: decide a classe `.row-click` (cursor, hover) e o handler
   *  na mesma expressão, senão a linha oferece uma afordância que não cumpre. */
  rowClickable?: (row: T) => boolean;
  /** Suprime o `<thead>` no desktop, para tabelas que nunca tiveram cabeçalho —
   *  renderizá-lo seria adicionar uma linha que não existia. O `header` de cada
   *  coluna continua sendo usado como rótulo no painel expandido do card. */
  hideHeader?: boolean;
  /** Conteúdo extra por linha, quando ela está expandida — um editor ou detalhe rico que
   *  não cabe no modelo de colunas. No desktop vira uma `<tr>` com um `<td colSpan>` logo
   *  ABAIXO da linha; no telefone, um bloco dentro do card expandido. Devolva `null` para
   *  as linhas que não têm conteúdo extra. */
  renderSubRow?: (row: T) => ReactNode;
}

export function ResponsiveTable<T>({
  rows,
  columns,
  rowKey,
  onRowClick,
  footLabel,
  rowClassName,
  rowClickable,
  hideHeader,
  renderSubRow,
}: ResponsiveTableProps<T>) {
  const phone = usePhone();

  if (import.meta.env.DEV) {
    const count = (r: ColumnRole) => columns.filter((c) => c.role === r).length;
    if (count('title') !== 1)
      console.warn(`ResponsiveTable: esperado exatamente 1 papel "title", encontrado ${count('title')}.`);
    if (count('amount') > 1)
      console.warn(
        `ResponsiveTable: ${count('amount')} colunas "amount"; só a primeira aparece no card. ` +
          `Reclassifique as demais como "detail".`,
      );
    if (count('meta') > 2) console.warn(`ResponsiveTable: ${count('meta')} colunas "meta"; acima de 2 o card trunca.`);

    // Validar chaves de linha
    const keys: (string | null | undefined)[] = [];
    const seenKeys = new Set<string>();
    for (const row of rows) {
      const key = rowKey(row);
      keys.push(key);
      if (key == null) {
        console.warn(
          `ResponsiveTable: rowKey retornou null ou undefined — todas as linhas compartilharão um único estado ` +
            `de expansão no telefone.`,
        );
        break; // avisar uma vez por render
      }
      if (seenKeys.has(key)) {
        console.warn(
          `ResponsiveTable: duas linhas geraram a chave "${key}" — elas compartilharão um único estado de ` +
            `expansão no telefone.`,
        );
        break; // avisar uma vez por render
      }
      seenKeys.add(key);
    }
  }

  if (phone)
    return (
      <CardList
        rows={rows}
        columns={columns}
        rowKey={rowKey}
        onRowClick={onRowClick}
        footLabel={footLabel}
        rowClassName={rowClassName}
        rowClickable={rowClickable}
        renderSubRow={renderSubRow}
      />
    );

  const hasFoot = footLabel != null || columns.some((c) => c.footCell);

  // Task 18 fix-round 1: `.rt-scroll` gives this table somewhere to go instead of being clipped by
  // `.panel { overflow: hidden }` whenever its columns don't fit — which the card layout hides below
  // 640px, but between 641px and roughly 1023px (a plain tablet width) this bare `<table>` is exactly
  // what renders, and until now nothing gave it a way to overflow visibly. Deliberately NOT
  // `.table-scroll`: that class also pins the first column (`position: sticky`) for the 4 accounting
  // sheets that opted into it on purpose — reusing it here would change the look of every migrated
  // table, not just the ones that actually overflow. `.rt-scroll` is `overflow-x: auto` alone, so a
  // table that already fits renders pixel-identical to before (no scrollbar, no visual change).
  return (
    <div className='rt-scroll'>
      <table>
        {!hideHeader && (
          <thead>
            <tr>
              {columns.map((c) => (
                <th key={c.key} style={c.align ? { textAlign: c.align } : undefined}>
                  {c.header}
                </th>
              ))}
            </tr>
          </thead>
        )}
        <tbody>
          {rows.map((row) => {
            const key = rowKey(row);
            const subRow = renderSubRow?.(row) ?? null;
            const clickable = onRowClick != null && (rowClickable?.(row) ?? true);
            return (
              <Fragment key={key}>
                <tr
                  className={cx(clickable && 'row-click', rowClassName?.(row))}
                  // Alvo do clique, não papel da coluna: só um controle interativo de verdade (botão,
                  // link, checkbox…) interrompe a navegação — o resto da célula (chip de status, espaço
                  // vazio ao lado do botão de ação) abre a linha como qualquer outra célula. Ver
                  // `onRowClick` em ui.tsx para o porquê disto substituir o `stopPropagation` manual.
                  onClick={clickable ? makeRowClickHandler(() => onRowClick(row)) : undefined}
                >
                  {columns.map((c) => (
                    <td key={c.key} style={c.align ? { textAlign: c.align } : undefined}>
                      {c.cell(row)}
                    </td>
                  ))}
                </tr>
                {subRow != null && (
                  <tr>
                    <td colSpan={columns.length}>{subRow}</td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </tbody>
        {hasFoot && (
          <tfoot>
            <tr>
              {columns.map((c, i) => (
                <td key={c.key} style={c.align ? { textAlign: c.align } : undefined}>
                  {/* A legenda ocupa a primeira coluna, como o antigo <td colSpan> — sem bordas verticais
                   *  entre <td>s, um <td> por coluna com a legenda só na primeira é indistinguível de um
                   *  <td colSpan> abrangendo as mesmas colunas. As demais mostram seu próprio footCell.
                   *  Se a coluna 0 também declarar footCell (footLabel ausente), ele não fica mudo:
                   *  footLabel vence quando presente, mas nunca engole silenciosamente um footCell real. */}
                  {i === 0 ? (footLabel ?? c.footCell?.(rows)) : c.footCell?.(rows)}
                </td>
              ))}
            </tr>
          </tfoot>
        )}
      </table>
    </div>
  );
}

function CardList<T>({
  rows,
  columns,
  rowKey,
  onRowClick,
  footLabel,
  rowClassName,
  rowClickable,
  renderSubRow,
}: ResponsiveTableProps<T>) {
  const { t } = useTranslation();
  const [open, setOpen] = useState<Record<string, boolean>>({});

  const byRole = (role: ColumnRole) => columns.filter((c) => c.role === role);
  const title = byRole('title')[0];
  const amount = byRole('amount')[0];
  const metas = byRole('meta');
  const statuses = byRole('status');
  const details = byRole('detail');
  const footColumns = columns.filter((c) => c.footCell);
  const hasFoot = footLabel != null || footColumns.length > 0;

  // Sem wrapper de stopPropagation: o alvo do clique decide (ver `onRowClick` em ui.tsx), não o papel
  // da coluna — o mesmo tratamento do <td> no desktop, ver acima.
  const cell = (c: Column<T>, row: T): ReactNode => c.cell(row);

  return (
    <div className='rt-cards'>
      {rows.map((row) => {
        const key = rowKey(row);
        const isOpen = !!open[key];
        const subRow = renderSubRow?.(row) ?? null;
        const clickable = onRowClick != null && (rowClickable?.(row) ?? true);
        return (
          <div key={key} className={cx('rt-card', rowClassName?.(row))}>
            <div
              className={clickable ? 'rt-head row-click' : 'rt-head'}
              onClick={clickable ? makeRowClickHandler(() => onRowClick(row)) : undefined}
            >
              <div className='rt-main'>
                {title && <div className='rt-title'>{cell(title, row)}</div>}
                {metas.length > 0 && (
                  <div className='rt-meta'>
                    {metas.map((c, i) => (
                      <span key={c.key}>
                        {i > 0 && ' · '}
                        {cell(c, row)}
                      </span>
                    ))}
                  </div>
                )}
                {statuses.length > 0 && (
                  <div className='rt-status'>
                    {statuses.map((c) => (
                      <span key={c.key}>{cell(c, row)}</span>
                    ))}
                  </div>
                )}
              </div>
              <div className='rt-right'>
                {amount && <div className='rt-amount'>{cell(amount, row)}</div>}
                {(details.length > 0 || subRow != null) && (
                  <button
                    type='button'
                    className='rt-toggle'
                    aria-expanded={isOpen}
                    aria-label={isOpen ? t('table.collapseRow') : t('table.expandRow')}
                    onClick={(e) => {
                      e.stopPropagation(); // não abrir o detalhe da linha ao expandir
                      setOpen((o) => ({ ...o, [key]: !o[key] }));
                    }}
                  >
                    <ChevronDown className={isOpen ? 'rt-chev open' : 'rt-chev'} />
                  </button>
                )}
              </div>
            </div>
            {isOpen && details.length > 0 && (
              <div className='rt-detail'>
                {details.map((c) => (
                  <div key={c.key}>
                    <span className='rt-k'>{c.header}</span>
                    <div className='rt-v'>{cell(c, row)}</div>
                  </div>
                ))}
              </div>
            )}
            {isOpen && subRow != null && <div className='rt-subrow'>{subRow}</div>}
          </div>
        );
      })}
      {hasFoot && (
        // Resumo dos totais: mesmo cabeçalho (.rt-head/.rt-title) e mesma grade rótulo/valor de um
        // `.rt-detail` expandido que qualquer outro card — a legenda (footLabel) é o título do card,
        // nunca o header emprestado de uma coluna, então "Totais" nunca aparece rotulado como
        // "CÓDIGO: Totais". As colunas sem footCell simplesmente não entram na grade.
        <div className='rt-card'>
          {footLabel != null && (
            <div className='rt-head'>
              <div className='rt-main'>
                <div className='rt-title'>{footLabel}</div>
              </div>
            </div>
          )}
          {footColumns.length > 0 && (
            <div className='rt-detail'>
              {footColumns.map((c) => (
                <div key={c.key}>
                  <span className='rt-k'>{c.header}</span>
                  <div className='rt-v'>{c.footCell?.(rows)}</div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

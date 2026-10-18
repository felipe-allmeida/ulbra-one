import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '../i18n';
import { renderWithProviders } from '../test/renderWithProviders';
import { setViewportMatches } from '../test/setup';
import { ResponsiveTable, type Column } from './ResponsiveTable';

interface Row {
  id: string;
  desc: string;
  vendor: string;
  amount: string;
  costCenter: string;
}

const ROWS: Row[] = [
  { id: '1', desc: 'Manutenção predial', vendor: 'HORIZONTE LTDA', amount: 'R$ 12.345,67', costCenter: '3.02 — Infra' },
  { id: '2', desc: 'Licença de software', vendor: 'MICROSOFT', amount: 'R$ 48.900,00', costCenter: '1.01 — TI' },
];

const COLUMNS: Column<Row>[] = [
  { key: 'desc', header: 'Descrição', role: 'title', cell: (r) => r.desc },
  { key: 'vendor', header: 'Fornecedor', role: 'meta', cell: (r) => r.vendor },
  { key: 'amount', header: 'Valor', role: 'amount', cell: (r) => r.amount, align: 'right' },
  { key: 'cc', header: 'Centro de custo', role: 'detail', cell: (r) => r.costCenter },
];

const setup = (props: Partial<React.ComponentProps<typeof ResponsiveTable<Row>>> = {}) =>
  renderWithProviders(<ResponsiveTable rows={ROWS} columns={COLUMNS} rowKey={(r) => r.id} {...props} />);

describe('ResponsiveTable — desktop', () => {
  beforeEach(() => setViewportMatches(false));

  it('renderiza uma <table> com todas as colunas, inclusive as de papel detail', () => {
    setup();
    expect(document.querySelector('table')).toBeInTheDocument();
    for (const header of ['Descrição', 'Fornecedor', 'Valor', 'Centro de custo']) {
      expect(screen.getByRole('columnheader', { name: header })).toBeInTheDocument();
    }
    // detail não some no desktop — é coluna normal
    expect(screen.getByText('3.02 — Infra')).toBeInTheDocument();
  });

  it('não renderiza cards no desktop', () => {
    setup();
    expect(document.querySelector('.rt-card')).not.toBeInTheDocument();
  });

  it('dispara onRowClick ao clicar na linha', async () => {
    const onRowClick = vi.fn();
    setup({ onRowClick });
    await userEvent.click(screen.getByText('Manutenção predial'));
    expect(onRowClick).toHaveBeenCalledWith(ROWS[0]);
  });

  it('aplica o alinhamento declarado', () => {
    setup();
    expect(screen.getByRole('columnheader', { name: 'Valor' })).toHaveStyle({ textAlign: 'right' });
  });

  it('sem hideHeader, renderiza o <thead> normalmente (guarda contra mudar o padrão em silêncio)', () => {
    setup();
    expect(document.querySelector('thead')).toBeInTheDocument();
  });

  it('com hideHeader, não renderiza <thead> mas mantém o mesmo <tbody>', () => {
    setup({ hideHeader: true });
    expect(document.querySelector('thead')).not.toBeInTheDocument();
    expect(document.querySelector('tbody')).toBeInTheDocument();
    expect(screen.getByText('Manutenção predial')).toBeInTheDocument();
    expect(screen.getByText('Licença de software')).toBeInTheDocument();
    expect(document.querySelectorAll('tbody tr')).toHaveLength(ROWS.length);
  });
});

describe('ResponsiveTable — telefone', () => {
  beforeEach(() => setViewportMatches(true));

  it('não renderiza <table>', () => {
    setup();
    expect(document.querySelector('table')).not.toBeInTheDocument();
  });

  it('mostra title, amount e meta no cabeçalho do card', () => {
    setup();
    expect(screen.getByText('Manutenção predial')).toBeInTheDocument();
    expect(screen.getByText('R$ 12.345,67')).toBeInTheDocument();
    expect(screen.getByText(/HORIZONTE LTDA/)).toBeInTheDocument();
  });

  it('esconde as colunas detail até expandir', () => {
    setup();
    expect(screen.queryByText('3.02 — Infra')).not.toBeInTheDocument();
  });

  it('expande ao toque e revela as colunas detail com seus rótulos', async () => {
    setup();
    const toggles = screen.getAllByRole('button', { name: /expandir/i });
    await userEvent.click(toggles[0]);
    expect(screen.getByText('3.02 — Infra')).toBeInTheDocument();
    expect(screen.getByText('Centro de custo')).toBeInTheDocument();
  });

  it('expande cada linha de forma independente', async () => {
    setup();
    const toggles = screen.getAllByRole('button', { name: /expandir/i });
    await userEvent.click(toggles[0]);
    expect(screen.getByText('3.02 — Infra')).toBeInTheDocument();
    expect(screen.queryByText('1.01 — TI')).not.toBeInTheDocument();
  });

  it('recolhe ao tocar de novo', async () => {
    setup();
    await userEvent.click(screen.getAllByRole('button', { name: /expandir/i })[0]);
    await userEvent.click(screen.getByRole('button', { name: /recolher/i }));
    expect(screen.queryByText('3.02 — Infra')).not.toBeInTheDocument();
  });

  it('expõe aria-expanded no botão', async () => {
    setup();
    const toggle = screen.getAllByRole('button', { name: /expandir/i })[0];
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    expect(screen.getByRole('button', { name: /recolher/i })).toHaveAttribute('aria-expanded', 'true');
  });

  it('expandir não dispara onRowClick', async () => {
    const onRowClick = vi.fn();
    setup({ onRowClick });
    await userEvent.click(screen.getAllByRole('button', { name: /expandir/i })[0]);
    expect(onRowClick).not.toHaveBeenCalled();
  });

  it('sem colunas detail, não renderiza botão de expandir', () => {
    renderWithProviders(
      <ResponsiveTable rows={ROWS} columns={COLUMNS.filter((c) => c.role !== 'detail')} rowKey={(r) => r.id} />,
    );
    expect(screen.queryByRole('button', { name: /expandir/i })).not.toBeInTheDocument();
  });

  it('com hideHeader, o painel expandido do card ainda mostra o header de cada coluna detail como rótulo', async () => {
    setup({ hideHeader: true });
    const toggles = screen.getAllByRole('button', { name: /expandir/i });
    await userEvent.click(toggles[0]);
    expect(screen.getByText('Centro de custo')).toBeInTheDocument();
    expect(screen.getByText('3.02 — Infra')).toBeInTheDocument();
  });
});

describe('ResponsiveTable — onRowClick decide pelo alvo do clique, não pelo papel da coluna', () => {
  // Coluna própria (não reusa COLUMNS): só o mínimo para não acionar os avisos de cardinalidade
  // (1 title, 1 meta, nenhum amount) enquanto exercita um chip de status (não-interativo) ao lado
  // de um botão de ação de verdade, na mesma célula 'meta'.
  interface ActionRow {
    id: string;
    desc: string;
  }
  const ACTION_ROWS: ActionRow[] = [{ id: '1', desc: 'Item' }];
  const ACTION_COLUMNS: Column<ActionRow>[] = [
    { key: 'desc', header: 'Descrição', role: 'title', cell: (r) => r.desc },
    { key: 'status', header: 'Status', role: 'status', cell: () => <span>Aprovado</span> },
    {
      key: 'action',
      header: 'Ação',
      role: 'meta',
      // Espaço vazio ao lado do botão, dentro da MESMA célula — é exatamente o "zona morta" que o
      // `stopPropagation` na célula inteira criava (ver comentário de `onRowClick` em ui.tsx).
      cell: () => (
        <span>
          <button>Aprovar</button> <span data-testid='action-empty-space'>(pendente)</span>
        </span>
      ),
    },
  ];
  const setupActions = (phone: boolean, onRowClick: () => void) => {
    setViewportMatches(phone);
    renderWithProviders(
      <ResponsiveTable rows={ACTION_ROWS} columns={ACTION_COLUMNS} rowKey={(r) => r.id} onRowClick={onRowClick} />,
    );
  };

  it('clique num chip de status ABRE a linha — não é mais um controle que intercepta o clique (desktop)', async () => {
    const onRowClick = vi.fn();
    setupActions(false, onRowClick);
    await userEvent.click(screen.getByText('Aprovado'));
    expect(onRowClick).toHaveBeenCalledWith(ACTION_ROWS[0]);
  });

  it('clique num chip de status ABRE a linha (telefone)', async () => {
    const onRowClick = vi.fn();
    setupActions(true, onRowClick);
    await userEvent.click(screen.getByText('Aprovado'));
    expect(onRowClick).toHaveBeenCalledWith(ACTION_ROWS[0]);
  });

  it('clique no botão de ação NÃO abre a linha (desktop)', async () => {
    const onRowClick = vi.fn();
    setupActions(false, onRowClick);
    await userEvent.click(screen.getByText('Aprovar'));
    expect(onRowClick).not.toHaveBeenCalled();
  });

  it('clique no botão de ação NÃO abre a linha (telefone)', async () => {
    const onRowClick = vi.fn();
    setupActions(true, onRowClick);
    await userEvent.click(screen.getByText('Aprovar'));
    expect(onRowClick).not.toHaveBeenCalled();
  });

  // A melhoria em si: o espaço vazio ao lado do botão, na MESMA célula, costumava ser zona morta
  // (stopPropagation na célula inteira). Agora abre a linha como qualquer outro ponto dela.
  it('clique no espaço vazio ao lado do botão de ação ABRE a linha (desktop)', async () => {
    const onRowClick = vi.fn();
    setupActions(false, onRowClick);
    await userEvent.click(screen.getByTestId('action-empty-space'));
    expect(onRowClick).toHaveBeenCalledWith(ACTION_ROWS[0]);
  });

  it('clique no espaço vazio ao lado do botão de ação ABRE a linha (telefone)', async () => {
    const onRowClick = vi.fn();
    setupActions(true, onRowClick);
    await userEvent.click(screen.getByTestId('action-empty-space'));
    expect(onRowClick).toHaveBeenCalledWith(ACTION_ROWS[0]);
  });
});

describe('ResponsiveTable — footCell (linha de totais)', () => {
  // Coluna própria: 1 title + 1 amount (respeita a cardinalidade) mais duas colunas com footCell —
  // uma alinhada, para conferir que o rodapé do desktop respeita `align` como qualquer outro `<td>`.
  interface TotalsRow {
    id: string;
    label: string;
    value: number;
  }
  const TOTALS_ROWS: TotalsRow[] = [
    { id: '1', label: 'Linha 1', value: 100 },
    { id: '2', label: 'Linha 2', value: 50 },
  ];
  const sum = (rows: TotalsRow[]) => rows.reduce((acc, r) => acc + r.value, 0);
  const COLUMNS_WITH_FOOT: Column<TotalsRow>[] = [
    { key: 'label', header: 'Linha', role: 'title', cell: (r) => r.label },
    {
      key: 'value',
      header: 'Valor',
      role: 'amount',
      align: 'right',
      cell: (r) => `R$ ${r.value}`,
      footCell: (rows) => `R$ ${sum(rows)}`,
    },
  ];

  it('desktop: renderiza <tfoot> com uma célula por coluna, vazia onde não há footCell, respeitando align', () => {
    setViewportMatches(false);
    renderWithProviders(<ResponsiveTable rows={TOTALS_ROWS} columns={COLUMNS_WITH_FOOT} rowKey={(r) => r.id} />);
    const foot = document.querySelector('tfoot');
    expect(foot).toBeInTheDocument();
    const cells = foot!.querySelectorAll('td');
    expect(cells).toHaveLength(2);
    expect(cells[0]).toHaveTextContent(''); // "label" não tem footCell — célula vazia, mantém o grid alinhado
    expect(cells[1]).toHaveTextContent('R$ 150');
    expect(cells[1]).toHaveStyle({ textAlign: 'right' }); // herda o `align` da própria coluna
  });

  it('telefone: mostra um card de resumo só com as colunas que têm footCell, rotuladas pelo header', () => {
    setViewportMatches(true);
    renderWithProviders(<ResponsiveTable rows={TOTALS_ROWS} columns={COLUMNS_WITH_FOOT} rowKey={(r) => r.id} />);
    expect(screen.getByText('Valor', { selector: '.rt-k' })).toBeInTheDocument();
    expect(screen.getByText('R$ 150')).toBeInTheDocument();
    expect(screen.queryByText('Linha', { selector: '.rt-k' })).not.toBeInTheDocument(); // sem footCell, não aparece
  });

  it('desktop: com footLabel, a legenda ocupa a primeira célula e os footCell ficam em suas próprias colunas', () => {
    setViewportMatches(false);
    renderWithProviders(
      <ResponsiveTable rows={TOTALS_ROWS} columns={COLUMNS_WITH_FOOT} rowKey={(r) => r.id} footLabel='Total' />,
    );
    const foot = document.querySelector('tfoot');
    const cells = foot!.querySelectorAll('td');
    expect(cells[0]).toHaveTextContent('Total'); // legenda na primeira coluna, mesmo sem footCell nela
    expect(cells[1]).toHaveTextContent('R$ 150');
    expect(cells[1]).toHaveStyle({ textAlign: 'right' });
  });

  it('telefone: com footLabel, o card de resumo é titulado pela legenda — nunca pelo header de uma coluna', () => {
    setViewportMatches(true);
    renderWithProviders(
      <ResponsiveTable rows={TOTALS_ROWS} columns={COLUMNS_WITH_FOOT} rowKey={(r) => r.id} footLabel='Total' />,
    );
    expect(screen.getByText('Total', { selector: '.rt-title' })).toBeInTheDocument();
    expect(screen.getByText('Valor', { selector: '.rt-k' })).toBeInTheDocument();
    expect(screen.getByText('R$ 150')).toBeInTheDocument();
  });

  it('desktop: com footLabel e footCell na coluna 0, a legenda vence — footLabel nunca é engolido nem engole o footCell', () => {
    setViewportMatches(false);
    const columnsWithFootOnFirst: Column<TotalsRow>[] = [
      { ...COLUMNS_WITH_FOOT[0], footCell: () => 'Ignorado' },
      COLUMNS_WITH_FOOT[1],
    ];
    renderWithProviders(
      <ResponsiveTable rows={TOTALS_ROWS} columns={columnsWithFootOnFirst} rowKey={(r) => r.id} footLabel='Total' />,
    );
    const foot = document.querySelector('tfoot');
    const cells = foot!.querySelectorAll('td');
    expect(cells[0]).toHaveTextContent('Total');
    expect(cells[0]).not.toHaveTextContent('Ignorado');
  });

  it('desktop: footCell na coluna 0 sem footLabel aparece — a coluna 0 não fica muda só por ser a primeira', () => {
    setViewportMatches(false);
    const columnsWithFootOnFirst: Column<TotalsRow>[] = [
      { ...COLUMNS_WITH_FOOT[0], footCell: () => 'Soma da coluna 0' },
      COLUMNS_WITH_FOOT[1],
    ];
    renderWithProviders(<ResponsiveTable rows={TOTALS_ROWS} columns={columnsWithFootOnFirst} rowKey={(r) => r.id} />);
    const foot = document.querySelector('tfoot');
    const cells = foot!.querySelectorAll('td');
    expect(cells[0]).toHaveTextContent('Soma da coluna 0');
  });

  it('sem footLabel nem nenhuma coluna com footCell, não renderiza rodapé em nenhum tamanho', () => {
    setViewportMatches(false);
    const { unmount } = renderWithProviders(
      <ResponsiveTable
        rows={TOTALS_ROWS}
        columns={COLUMNS_WITH_FOOT.map((c) => ({ ...c, footCell: undefined }))}
        rowKey={(r) => r.id}
      />,
    );
    expect(document.querySelector('tfoot')).not.toBeInTheDocument();
    unmount();

    setViewportMatches(true);
    renderWithProviders(
      <ResponsiveTable
        rows={TOTALS_ROWS}
        columns={COLUMNS_WITH_FOOT.map((c) => ({ ...c, footCell: undefined }))}
        rowKey={(r) => r.id}
      />,
    );
    // No card de resumo, o único indício seria o rt-detail extra do rodapé — confere que não existe
    // nenhum '.rt-card' além dos 2 das linhas de dados.
    expect(document.querySelectorAll('.rt-card')).toHaveLength(TOTALS_ROWS.length);
  });
});

describe('ResponsiveTable — rowClassName', () => {
  it('aplica a classe extra no <tr> do desktop', () => {
    setViewportMatches(false);
    renderWithProviders(
      <ResponsiveTable
        rows={ROWS}
        columns={COLUMNS}
        rowKey={(r) => r.id}
        rowClassName={(r) => (r.id === '1' ? 'cf-row-danger' : undefined)}
      />,
    );
    const rows = document.querySelectorAll('tbody tr');
    expect(rows[0]).toHaveClass('cf-row-danger');
    expect(rows[1]).not.toHaveClass('cf-row-danger');
  });

  it('aplica a classe extra no .rt-card do telefone', () => {
    setViewportMatches(true);
    renderWithProviders(
      <ResponsiveTable
        rows={ROWS}
        columns={COLUMNS}
        rowKey={(r) => r.id}
        rowClassName={(r) => (r.id === '1' ? 'cf-row-danger' : undefined)}
      />,
    );
    const cards = document.querySelectorAll('.rt-card');
    expect(cards[0]).toHaveClass('cf-row-danger');
    expect(cards[1]).not.toHaveClass('cf-row-danger');
  });
});

describe('ResponsiveTable — rowClickable', () => {
  // A afordância e o handler saem do mesmo portão: uma linha que mostra cursor de clicável e não
  // navega é pior que uma linha inerte, porque promete.
  const clickableOnlyFirst = (r: Row) => r.id === '1';

  it('no desktop, só a linha permitida ganha .row-click e dispara', async () => {
    setViewportMatches(false);
    const onRowClick = vi.fn();
    setup({ onRowClick, rowClickable: clickableOnlyFirst });

    const rows = document.querySelectorAll('tbody tr');
    expect(rows[0]).toHaveClass('row-click');
    expect(rows[1]).not.toHaveClass('row-click');

    await userEvent.click(screen.getByText('Licença de software'));
    expect(onRowClick).not.toHaveBeenCalled();

    await userEvent.click(screen.getByText('Manutenção predial'));
    expect(onRowClick).toHaveBeenCalledWith(ROWS[0]);
  });

  it('no telefone, só o card permitido ganha .row-click e dispara', async () => {
    setViewportMatches(true);
    const onRowClick = vi.fn();
    setup({ onRowClick, rowClickable: clickableOnlyFirst });

    const heads = document.querySelectorAll('.rt-head');
    expect(heads[0]).toHaveClass('row-click');
    expect(heads[1]).not.toHaveClass('row-click');

    await userEvent.click(screen.getByText('Licença de software'));
    expect(onRowClick).not.toHaveBeenCalled();

    await userEvent.click(screen.getByText('Manutenção predial'));
    expect(onRowClick).toHaveBeenCalledWith(ROWS[0]);
  });

  it('sem rowClickable, toda linha continua navegando', async () => {
    setViewportMatches(false);
    const onRowClick = vi.fn();
    setup({ onRowClick });

    const rows = document.querySelectorAll('tbody tr');
    expect(rows[0]).toHaveClass('row-click');
    expect(rows[1]).toHaveClass('row-click');

    await userEvent.click(screen.getByText('Licença de software'));
    expect(onRowClick).toHaveBeenCalledWith(ROWS[1]);
  });
});

describe('ResponsiveTable — cardinalidade de papéis', () => {
  it('avisa em dev quando há mais de uma coluna "amount"', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const columnsWithTwoAmounts: Column<Row>[] = [
      ...COLUMNS,
      { key: 'net', header: 'Valor líquido', role: 'amount', cell: (r) => r.amount },
    ];
    renderWithProviders(<ResponsiveTable rows={ROWS} columns={columnsWithTwoAmounts} rowKey={(r) => r.id} />);
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('amount'));
    warnSpy.mockRestore();
  });
});

describe('ResponsiveTable — renderSubRow', () => {
  // Só a linha '1' tem conteúdo extra; '2' devolve null — cobre o caso de nenhuma sub-linha.
  const renderSubRow = (r: Row) => (r.id === '1' ? <span>Editor de {r.desc}</span> : null);

  it('desktop: renderiza uma <tr><td colSpan> logo abaixo da linha com conteúdo, e nenhuma para a linha sem', () => {
    setViewportMatches(false);
    renderWithProviders(
      <ResponsiveTable rows={ROWS} columns={COLUMNS} rowKey={(r) => r.id} renderSubRow={renderSubRow} />,
    );

    const subCell = screen.getByText('Editor de Manutenção predial').closest('td');
    expect(subCell).toHaveAttribute('colSpan', String(COLUMNS.length));

    // A sub-linha vem logo depois da linha do próprio dono, não em qualquer outro lugar.
    const allRows = [...document.querySelectorAll('tbody tr')];
    const ownerRowIndex = allRows.findIndex((tr) => tr.textContent?.includes('Manutenção predial'));
    expect(allRows[ownerRowIndex + 1].textContent).toContain('Editor de Manutenção predial');

    // A linha '2' (sem conteúdo) não ganha nenhuma <tr> extra.
    expect(screen.queryByText(/Editor de Licença/)).not.toBeInTheDocument();
    expect(allRows).toHaveLength(ROWS.length + 1); // 2 linhas de dados + 1 sub-linha
  });

  it('telefone: some dentro do card expandido, depois dos campos detail, só quando a linha tem conteúdo', async () => {
    setViewportMatches(true);
    renderWithProviders(
      <ResponsiveTable rows={ROWS} columns={COLUMNS} rowKey={(r) => r.id} renderSubRow={renderSubRow} />,
    );

    // Fechado: não aparece.
    expect(screen.queryByText('Editor de Manutenção predial')).not.toBeInTheDocument();

    const toggles = screen.getAllByRole('button', { name: /expandir/i });
    await userEvent.click(toggles[0]); // linha '1'
    expect(screen.getByText('Editor de Manutenção predial')).toBeInTheDocument();
    // Aparece depois do bloco de detail, dentro do mesmo card.
    const card = screen.getByText('Editor de Manutenção predial').closest('.rt-card');
    const detail = card!.querySelector('.rt-detail');
    const subrow = card!.querySelector('.rt-subrow');
    expect(detail).toBeInTheDocument();
    expect(subrow).toBeInTheDocument();
    expect(detail!.compareDocumentPosition(subrow!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    await userEvent.click(toggles[1]); // linha '2', sem conteúdo
    expect(screen.queryByText(/Editor de Licença/)).not.toBeInTheDocument();
  });
});

describe('ResponsiveTable — validação de chaves', () => {
  it('avisa quando rowKey retorna undefined', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    setViewportMatches(true); // telefone para usar CardList
    renderWithProviders(
      <ResponsiveTable rows={ROWS} columns={COLUMNS} rowKey={() => undefined as unknown as string} />,
    );
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('null ou undefined'));
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('expansão'));
    warnSpy.mockRestore();
  });

  it('avisa quando duas linhas compartilham a mesma chave, nomeando-a', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    setViewportMatches(true);
    renderWithProviders(<ResponsiveTable rows={ROWS} columns={COLUMNS} rowKey={() => 'shared-key'} />);
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('shared-key'));
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('duas linhas'));
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('expansão'));
    warnSpy.mockRestore();
  });

  it('não avisa quando as chaves são únicas e bem-formadas', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    setViewportMatches(true);
    renderWithProviders(<ResponsiveTable rows={ROWS} columns={COLUMNS} rowKey={(r) => r.id} />);
    expect(warnSpy).not.toHaveBeenCalledWith(expect.stringContaining('rowKey'));
    warnSpy.mockRestore();
  });
});

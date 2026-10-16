import type { ReactNode } from 'react';
import {
  Workflow,
  Users,
  ListChecks,
  PiggyBank,
  Lightbulb,
  FileText,
  ClipboardCheck,
  Wallet,
  CheckCircle2,
  Banknote,
  Calculator,
  ArrowLeftRight,
  UserSquare,
  BookText,
} from 'lucide-react';

/**
 * User manual content (fluxo + cargos), authored as typed pt-BR data — the single deliberate
 * exception to the "no hardcoded pt-BR in JSX" rule (see the page's sibling module). The app is
 * pt-BR-only; keeping long-form prose here (instead of the i18n catalog) keeps it maintainable and
 * lets us use rich blocks. Identifiers/comments stay in English. Every process statement below is
 * validated against the real Ulbra.One behavior (Budgeting/Finance/Notifications), NOT copied from
 * the legacy "orçamento" manual — the two diverge (payment plan before approval, no separate tax
 * step, auto-scheduled payable, hard budget block, etc.).
 *
 * Inline emphasis: wrap a span in **double asterisks** — the renderer turns it bold.
 */

export type CalloutTone = 'info' | 'warning' | 'danger' | 'success';

/** A node in the overview flow diagram. `stage` (1..6) picks the accent color; 0 = neutral (end). */
export interface FlowNode {
  n: number;
  title: string;
  role: string;
  stage: number;
}

/** A "desvio de fluxo" card (branch/exception in the process). */
export interface Deviation {
  tone: CalloutTone;
  title: string;
  text: string;
  from: string;
  to: string;
}

/** A cargo card in the roles section. */
export interface RoleCard {
  name: string;
  summary: string;
  points: string[];
}

/** A detailed step card ("Etapa N"). */
export interface DocStep {
  n: string;
  stage: number;
  eyebrow: string;
  title: string;
  icon: ReactNode;
  status: string;
  meta: { label: string; value: string }[];
  doItems: string[];
  rules: { label: string; text: string }[];
}

/** Content blocks the renderer knows how to draw. */
export type Block =
  | { kind: 'p'; text: string }
  | { kind: 'heading'; text: string }
  | { kind: 'list'; items: string[]; ordered?: boolean }
  | { kind: 'callout'; tone: CalloutTone; title: string; text: string }
  | { kind: 'todo'; text: string }
  | { kind: 'flow'; nodes: FlowNode[] }
  | { kind: 'deviations'; items: Deviation[] }
  | { kind: 'roles'; items: RoleCard[] }
  | { kind: 'steps'; steps: DocStep[] }
  | { kind: 'table'; head: string[]; rows: string[][] }
  | { kind: 'cards'; caption: string; items: { emoji: string; title: string }[] }
  // Inline preview rendered with the app's REAL components (not a screenshot): 'tabs' shows the
  // Gestão Financeira tab bar (StageTabBar) with `active` highlighted; 'selectCompany' shows the
  // "Selecione uma empresa" empty state that the screen renders before a company is chosen.
  | { kind: 'screen'; variant: 'tabs' | 'selectCompany'; active?: string; caption?: string };

export interface DocSection {
  id: string;
  navLabel: string;
  navSub: string;
  icon: ReactNode;
  title: string;
  sub: string;
  /** When set, the section is restricted: only users passing the matching predicate see it in the TOC
   * and can open it. 'financeManual' = Controladoria/Financeiro/Presidência/Admin (canSeeFinanceManual). */
  gated?: 'financeManual';
  blocks: Block[];
}

/** Fixed structural labels of the manual UI. Kept here (not i18n) so every pt-BR manual string lives
 * behind this single, documented boundary — the page title/nav labels still go through i18n. */
export const DOC_LABELS = {
  pageSubtitle: 'Manuais de uso: fluxo, cargos e regras do sistema.',
  doTitle: 'O que fazer',
  rulesTitle: 'Regras',
  todoBadge: 'Em revisão',
  quemAtua: 'Quem atua',
} as const;

export const DOC_SECTIONS: DocSection[] = [
  // ------------------------------------------------------------------ Visão Geral
  {
    id: 'visao-geral',
    navLabel: 'Visão Geral do Fluxo',
    navSub: 'Do pedido ao pagamento',
    icon: <Workflow />,
    title: 'Visão Geral do Fluxo',
    sub: 'O caminho de uma despesa, da solicitação à contabilização',
    blocks: [
      {
        kind: 'p',
        text: 'O Ulbra.One organiza toda despesa em um fluxo único, do pedido até a contabilização. Cada etapa tem um responsável, e o sistema só deixa avançar quando a etapa anterior foi concluída. O diagrama abaixo mostra o caminho normal de uma requisição.',
      },
      {
        kind: 'flow',
        nodes: [
          { n: 1, title: 'Solicitação', role: 'Solicitante', stage: 1 },
          { n: 2, title: 'Triagem', role: 'Controladoria', stage: 2 },
          { n: 3, title: 'Complemento Financeiro', role: 'Solicitante', stage: 3 },
          { n: 4, title: 'Aprovação', role: 'Aprovador', stage: 4 },
          { n: 5, title: 'Pagamento', role: 'Financeiro', stage: 5 },
          { n: 6, title: 'Contabilização', role: 'Contábil', stage: 6 },
          { n: 7, title: 'Finalizada', role: 'Sistema', stage: 0 },
        ],
      },
      { kind: 'heading', text: 'Desvios e situações especiais' },
      {
        kind: 'deviations',
        items: [
          {
            tone: 'warning',
            title: 'Devolução na triagem',
            text: 'Se faltar documento ou algum dado estiver errado, a Controladoria devolve a requisição para o Solicitante corrigir. Depois de corrigida, ela volta para a triagem.',
            from: 'Triagem',
            to: 'Complemento',
          },
          {
            tone: 'info',
            title: 'Reabertura da triagem',
            text: 'Ao detalhar o pagamento, se o valor total ficar maior que o aprovado na triagem — ou se o fornecedor for trocado — a requisição volta para a triagem para nova conferência.',
            from: 'Complemento',
            to: 'Triagem',
          },
          {
            tone: 'danger',
            title: 'Estouro de orçamento',
            text: 'Na aprovação, se a requisição ultrapassar o teto do orçamento da competência, o sistema bloqueia. Para liberar, é preciso uma Adequação Orçamentária. Contas Fixas não passam por essa trava.',
            from: 'Aprovação',
            to: 'Adequação',
          },
          {
            tone: 'warning',
            title: 'Reversão de pagamento',
            text: 'Se um pagamento já baixado for estornado, a requisição correspondente reabre e volta para "Em pagamento" até ser quitada novamente.',
            from: 'Paga',
            to: 'Em pagamento',
          },
        ],
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Prazo de atendimento (SLA)',
        text: 'As etapas de Triagem e Aprovação têm prazo de **24 horas úteis**. A contagem considera apenas dias úteis, excluindo fins de semana e feriados nacionais. O sistema acompanha esse prazo e sinaliza quando ele estoura.',
      },
    ],
  },

  // ------------------------------------------------------------------ Cargos
  {
    id: 'cargos',
    navLabel: 'Cargos e Responsabilidades',
    navSub: 'O que cada perfil faz',
    icon: <Users />,
    title: 'Cargos e Responsabilidades',
    sub: 'O que cada cargo pode fazer no sistema',
    blocks: [
      {
        kind: 'p',
        text: 'Cada pessoa usa o Ulbra.One de acordo com o seu cargo. O cargo define o que aparece no menu e quais ações estão liberadas. Veja o que cada um faz no dia a dia.',
      },
      {
        kind: 'roles',
        items: [
          {
            name: 'Solicitante',
            summary: 'Abre e acompanha as próprias requisições.',
            points: [
              'Cria a requisição escolhendo o tipo (Compra, Prestação de Serviço, Conta Fixa ou Projeto), a unidade, a natureza orçamentária e o fornecedor.',
              'Cadastra o fornecedor pelo CNPJ na hora, se ele ainda não existir.',
              'Após a triagem, informa o plano de pagamento (parcelas, vencimentos e forma de pagamento) e anexa a nota fiscal.',
              'Enxerga apenas as unidades e naturezas orçamentárias do seu escopo.',
            ],
          },
          {
            name: 'Controladoria',
            summary: 'Faz a triagem das requisições.',
            points: [
              'Confere fornecedor, unidade, natureza orçamentária, valores e documentos de cada requisição que chega.',
              'Encaminha para o complemento financeiro quando está tudo certo, ou devolve para o Solicitante corrigir.',
              'Pode corrigir o cadastro do fornecedor durante a triagem.',
              'Também aprova contas a pagar avulsas e participa da esteira das adequações orçamentárias.',
            ],
          },
          {
            name: 'Aprovador',
            summary: 'Dá a aprovação final da requisição.',
            points: [
              'Aprova ou reprova as requisições que passaram pela triagem, dentro da sua unidade e natureza orçamentária.',
              'Vê o saldo do orçamento no momento da decisão.',
              'Não tem limite de valor (alçada), mas não pode passar do teto do orçamento — um estouro exige Adequação.',
              'Trabalha pela sua Caixa de entrada.',
            ],
          },
          {
            name: 'Contábil',
            summary: 'Classifica e contabiliza as despesas.',
            points: [
              'Revisa a classificação contábil sugerida e registra a Nota Fiscal.',
              'Contabiliza (lança no Livro Diário) sob o regime de competência.',
              'Cuida das retenções de impostos (IRRF, INSS, ISS, PIS/COFINS/CSLL).',
              'Apenas consulta as requisições — não aprova nem paga.',
            ],
          },
          {
            name: 'Financeiro',
            summary: 'Agenda e paga (Gestão Financeira).',
            points: [
              'Vê a fila de pagamentos, agenda e efetua os pagamentos.',
              'Gera a remessa bancária (CNAB 240) e concilia o arquivo de retorno.',
              'Faz a conciliação bancária.',
              'Paga, mas não aprova — para garantir a separação de funções.',
            ],
          },
          {
            name: 'Presidência',
            summary: 'Visão ampla e aprovação de alto nível.',
            points: [
              'Enxerga todas as empresas, unidades e naturezas orçamentárias.',
              'Tem quase todas as permissões, exceto o gerenciamento de usuários.',
            ],
          },
          {
            name: 'Admin',
            summary: 'Administra o sistema.',
            points: [
              'Gerencia usuários, cargos e permissões.',
              'Tem acesso total e pode usar o "Ver como" para enxergar o sistema na visão de outro cargo.',
            ],
          },
        ],
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Escopo por unidade',
        text: 'Somente o **Solicitante** e o **Aprovador** têm a visão limitada por Empresa × Unidade × Natureza Orçamentária. Os demais cargos de gestão (Controladoria, Financeiro, Contábil, Presidência e Admin) podem usar a visão consolidada "Todas as empresas".',
      },
      {
        kind: 'callout',
        tone: 'warning',
        title: 'E o cargo "Usuário"?',
        text: '"Usuário" é o cargo base, sem permissões. Quem está apenas com esse cargo ainda não tem acesso liberado e vê uma tela informando que o acesso está sendo configurado por um administrador. Por isso, esta Documentação não aparece para ele.',
      },
    ],
  },

  // ------------------------------------------------------------------ Etapas
  {
    id: 'etapas',
    navLabel: 'Etapas Detalhadas',
    navSub: 'Passo a passo de cada fase',
    icon: <ListChecks />,
    title: 'Etapas Detalhadas',
    sub: 'O que fazer e as regras de cada etapa',
    blocks: [
      {
        kind: 'p',
        text: 'Aqui está o detalhe de cada etapa: quem atua, o que precisa ser feito e as regras que o sistema aplica.',
      },
      {
        kind: 'steps',
        steps: [
          {
            n: '1',
            stage: 1,
            eyebrow: 'Início do fluxo',
            title: 'Etapa 1 — Solicitação',
            icon: <FileText />,
            status: 'Rascunho → Triagem',
            meta: [
              { label: 'Quem atua', value: 'Solicitante' },
              { label: 'Tipo do pedido', value: 'Compra, Serviço, Conta Fixa ou Projeto' },
            ],
            doItems: [
              'Criar a requisição informando a unidade, a natureza orçamentária, a competência e o fornecedor (obrigatório).',
              'Escolher o tipo: Compra, Prestação de Serviço, Conta Fixa ou Projeto.',
              'Informar o valor estimado e a data do primeiro vencimento.',
              'Anexar os documentos de apoio (contrato, orçamento, proposta).',
              'Enviar para a triagem.',
            ],
            rules: [
              {
                label: 'Fornecedor pelo CNPJ',
                text: 'O sistema busca os dados do fornecedor automaticamente pelo CNPJ. Se ele ainda não existir, dá para cadastrar na hora.',
              },
              {
                label: 'Valor estimado',
                text: 'Nesta etapa o valor ainda é uma estimativa — o detalhamento das parcelas vem depois, no Complemento Financeiro.',
              },
            ],
          },
          {
            n: '2',
            stage: 2,
            eyebrow: 'Conferência',
            title: 'Etapa 2 — Triagem',
            icon: <ClipboardCheck />,
            status: 'Triagem',
            meta: [
              { label: 'Quem atua', value: 'Controladoria' },
              { label: 'Prazo (SLA)', value: '24 horas úteis' },
            ],
            doItems: [
              'Conferir os dados da requisição: fornecedor, unidade, natureza orçamentária, valores e documentos.',
              'Encaminhar para o Complemento Financeiro quando estiver tudo correto.',
              'Devolver para o Solicitante quando houver pendências.',
            ],
            rules: [
              {
                label: 'Prazo de 24h úteis',
                text: 'A contagem começa quando a requisição entra na triagem e considera apenas dias úteis.',
              },
              {
                label: 'Registro do valor',
                text: 'Ao aprovar a triagem, o valor e o fornecedor ficam registrados. Mudanças relevantes depois disso fazem a requisição voltar para a triagem.',
              },
            ],
          },
          {
            n: '3',
            stage: 3,
            eyebrow: 'Detalhamento do pagamento',
            title: 'Etapa 3 — Complemento Financeiro',
            icon: <Wallet />,
            status: 'Complemento financeiro → Aprovação',
            meta: [
              { label: 'Quem atua', value: 'Solicitante' },
              { label: 'Documento', value: 'Nota fiscal / fatura' },
            ],
            doItems: [
              'Montar o plano de pagamento: cada parcela com vencimento, valor e forma de pagamento (PIX, boleto ou transferência).',
              'Informar a data de emissão do documento.',
              'Anexar a nota fiscal ou o boleto correspondente.',
              'Enviar para a aprovação.',
            ],
            rules: [
              {
                label: 'Antes da aprovação',
                text: 'No Ulbra.One o plano de pagamento é feito ANTES da aprovação final — diferente do sistema antigo, em que a programação vinha depois.',
              },
              {
                label: 'Volta para a triagem',
                text: 'Se a soma das parcelas ficar acima do valor aprovado na triagem, ou se o fornecedor mudar, a requisição volta para a triagem.',
              },
              {
                label: 'Sem documento ainda',
                text: 'Se a nota fiscal não estiver disponível na hora, a parcela fica pendente de documento e a NF é incluída depois, para a contabilização.',
              },
            ],
          },
          {
            n: '4',
            stage: 4,
            eyebrow: 'Decisão',
            title: 'Etapa 4 — Aprovação',
            icon: <CheckCircle2 />,
            status: 'Aprovação → Em pagamento',
            meta: [
              { label: 'Quem atua', value: 'Aprovador' },
              { label: 'Prazo (SLA)', value: '24 horas úteis' },
            ],
            doItems: [
              'Analisar a requisição que passou pela triagem e pelo complemento financeiro.',
              'Aprovar (segue para pagamento) ou reprovar com justificativa.',
            ],
            rules: [
              {
                label: 'Trava de orçamento',
                text: 'A aprovação é bloqueada se a requisição ultrapassar o teto do orçamento da competência. Não há como forçar — o estouro só se resolve com uma Adequação Orçamentária.',
              },
              {
                label: 'Conta Fixa',
                text: 'Requisições de Conta Fixa não passam por essa trava, porque o valor mensal varia (por exemplo, energia e água).',
              },
              {
                label: 'Gera a conta a pagar',
                text: 'Ao aprovar, o sistema cria automaticamente uma conta a pagar no Financeiro, já com os dados da requisição.',
              },
            ],
          },
          {
            n: '5',
            stage: 5,
            eyebrow: 'Tesouraria',
            title: 'Etapa 5 — Pagamento',
            icon: <Banknote />,
            status: 'Agendada → Paga',
            meta: [
              { label: 'Quem atua', value: 'Financeiro' },
              { label: 'Integração', value: 'Remessa CNAB 240' },
            ],
            doItems: [
              'Acompanhar a fila de pagamentos (as contas geradas por requisições já entram agendadas).',
              'Gerar a remessa bancária (CNAB 240) e enviá-la ao banco.',
              'Conciliar o arquivo de retorno (.RET) e dar baixa, marcando cada pagamento como Pago com o comprovante.',
            ],
            rules: [
              {
                label: 'Já vem aprovada',
                text: 'A conta gerada por uma requisição pula a classificação e a aprovação do Financeiro, porque a requisição já foi aprovada na esteira.',
              },
              {
                label: 'Conta Fixa aguarda NF',
                text: 'Uma conta a pagar de Conta Fixa fica em "NF pendente" até a nota fiscal ser anexada.',
              },
            ],
          },
          {
            n: '6',
            stage: 6,
            eyebrow: 'Fechamento',
            title: 'Etapa 6 — Contabilização',
            icon: <Calculator />,
            status: 'Paga → Finalizada',
            meta: [
              { label: 'Quem atua', value: 'Contábil' },
              { label: 'Regime', value: 'Competência' },
            ],
            doItems: [
              'Revisar a nota fiscal e a classificação contábil de cada pagamento.',
              'Registrar a despesa e contabilizar (lançar no Livro Diário).',
              'Devolver ao Solicitante, com justificativa, se a nota estiver incorreta ou ilegível.',
            ],
            rules: [
              {
                label: 'Retenções',
                text: 'As retenções são calculadas conforme o momento: IRRF, INSS e ISS na emissão; PIS/COFINS/CSLL na baixa (rateadas).',
              },
              {
                label: 'Finalização',
                text: 'Quando todas as parcelas e notas são contabilizadas, a requisição passa automaticamente para Finalizada.',
              },
            ],
          },
        ],
      },
      { kind: 'heading', text: 'Status da Requisição' },
      {
        kind: 'table',
        head: ['Status', 'O que significa'],
        rows: [
          ['Rascunho', 'Sendo criada pelo Solicitante, ainda não enviada.'],
          ['Triagem', 'Em conferência pela Controladoria.'],
          ['Complemento', 'Devolvida ao Solicitante para correção.'],
          ['Complemento financeiro', 'Solicitante detalhando as parcelas de pagamento.'],
          ['Aprovação', 'Aguardando a decisão do Aprovador.'],
          ['Em pagamento', 'Aprovada; virou conta a pagar no Financeiro.'],
          ['Aguardando doc. fiscal', 'Esperando a nota fiscal para seguir.'],
          ['Paga', 'Todas as parcelas foram pagas.'],
          ['Finalizada', 'Contabilizada e concluída.'],
          ['Cancelada', 'Encerrada sem seguir o fluxo.'],
        ],
      },
      { kind: 'heading', text: 'Status da Conta a Pagar' },
      {
        kind: 'table',
        head: ['Status', 'O que significa'],
        rows: [
          ['Recebida', 'Acabou de entrar no Financeiro.'],
          ['Aguardando classificação', 'Precisa da classificação contábil.'],
          ['Aguardando aprovação', 'Espera a aprovação (contas avulsas).'],
          ['Aguardando contabilidade', 'Espera o registro contábil / a nota fiscal.'],
          ['Agendada', 'Pronta e agendada para pagamento.'],
          ['Paga', 'Pagamento efetuado e conciliado.'],
          ['Rejeitada / Cancelada', 'Encerrada sem pagamento.'],
        ],
      },
    ],
  },

  // ------------------------------------------------------------------ Orçamento
  {
    id: 'orcamento',
    navLabel: 'Orçamento e Adequações',
    navSub: 'Teto, saldo e ajustes',
    icon: <PiggyBank />,
    title: 'Orçamento e Adequações',
    sub: 'Como o teto controla os gastos e como ajustá-lo',
    blocks: [
      { kind: 'heading', text: 'Como funciona o teto' },
      {
        kind: 'p',
        text: 'O orçamento é organizado por Unidade × Natureza Orçamentária × Competência (o mês, no formato AAAA-MM). Cada combinação tem um teto — o valor máximo que pode ser gasto naquele mês.',
      },
      {
        kind: 'p',
        text: 'O saldo é simples: **disponível = orçado − consumido**. O consumido conta apenas as requisições que já chegaram ao pagamento (de "Em pagamento" em diante). As requisições ainda na esteira (triagem, complemento, aprovação) não reservam orçamento — aparecem só como uma projeção de "após aprovação".',
      },
      {
        kind: 'callout',
        tone: 'warning',
        title: 'Estouro do teto',
        text: 'Se uma requisição fizer o consumo passar do teto, a aprovação é bloqueada. Para liberar, é preciso aumentar o teto com uma **Adequação Orçamentária**.',
      },
      { kind: 'heading', text: 'Adequação Orçamentária' },
      {
        kind: 'p',
        text: 'A Adequação é o pedido formal para mudar o teto do orçamento. Existem três tipos:',
      },
      {
        kind: 'list',
        items: [
          '**Suplementação** — aumenta o teto de uma competência.',
          '**Redução** — diminui o teto de uma competência.',
          '**Transferência** — move valor entre duas competências da mesma Unidade e Natureza Orçamentária.',
        ],
      },
      {
        kind: 'p',
        text: 'A Adequação segue a própria esteira: **Aguardando triagem → Aguardando aprovação → Aprovada**. O teto só muda de verdade quando ela é aprovada. Também pode ser Revertida ou Cancelada.',
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Quem aprova a Adequação',
        text: 'A aprovação da Adequação depende do limite (alçada) do usuário e de uma permissão específica, liberada individualmente por um administrador.',
      },
    ],
  },

  // ------------------------------------------------------------------ Outras Orientações
  {
    id: 'orientacoes',
    navLabel: 'Outras Orientações',
    navSub: 'Regras do dia a dia',
    icon: <Lightbulb />,
    title: 'Outras Orientações',
    sub: 'Detalhes importantes do preenchimento',
    blocks: [
      { kind: 'heading', text: 'Conta Fixa' },
      {
        kind: 'p',
        text: 'Conta Fixa é a despesa recorrente de valor variável. Ela tem um tratamento próprio:',
      },
      {
        kind: 'list',
        items: [
          'Não passa pela trava de orçamento, porque o valor muda de mês para mês.',
          'É única por combinação de fornecedor, unidade e natureza orçamentária — não se abre duas para o mesmo trio.',
          'Aceita boleto e convênio, e a fatura pode ficar pendente até a nota fiscal chegar.',
        ],
      },
      {
        kind: 'cards',
        caption: 'Exemplos comuns de conta fixa:',
        items: [
          { emoji: '⚡', title: 'Energia' },
          { emoji: '💧', title: 'Água' },
          { emoji: '📞', title: 'Telefone' },
          { emoji: '🌐', title: 'Internet' },
        ],
      },
      { kind: 'heading', text: 'Como lançar o valor (bruto × líquido)' },
      { kind: 'p', text: 'O valor a lançar depende do tipo da despesa:' },
      {
        kind: 'list',
        items: [
          '**Compra** — lançar pelo **valor líquido** da nota. Não passa por análise de retenção e segue direto para o pagamento.',
          '**Prestação de Serviço ou Conta Fixa** — lançar pelo **valor bruto** da nota. A retenção de impostos é tratada na contabilização.',
        ],
      },
      { kind: 'heading', text: 'Notificações (o sino)' },
      {
        kind: 'p',
        text: 'O sistema avisa cada responsável quando a requisição chega até ele. Por exemplo: a Controladoria é avisada quando algo entra na triagem; o Aprovador, quando chega para aprovação; e o Solicitante, quando a requisição é devolvida, aprovada ou cancelada.',
      },
      { kind: 'heading', text: 'Prazos (SLA)' },
      {
        kind: 'p',
        text: 'As etapas de Triagem e Aprovação têm prazo de **24 horas úteis**. A contagem usa o fuso de Brasília e exclui fins de semana e feriados nacionais. O sistema acompanha e sinaliza quando o prazo estoura.',
      },
      {
        kind: 'todo',
        text: 'Alguns pontos do manual antigo — canal de suporte, procedimento de cobertura de férias e prazo mínimo de antecedência para agendar pagamentos — ainda serão confirmados para o Ulbra.One e incluídos aqui.',
      },
    ],
  },

  // ------------------------------------------------------- Guia do Solicitante (todos os cargos)
  {
    id: 'guia-solicitante',
    navLabel: 'Guia do Solicitante',
    navSub: 'Abertura e acompanhamento',
    icon: <UserSquare />,
    title: 'Guia do Solicitante',
    sub: 'Como abrir e acompanhar uma requisição',
    blocks: [
      {
        kind: 'p',
        text: 'O **Solicitante** abre as requisições de compra ou serviço e as acompanha até o pagamento. As demais etapas — triagem, aprovação, pagamento e contabilização — são de outras áreas; o acompanhamento se dá pelo status e pelas notificações.',
      },
      { kind: 'heading', text: 'Atuação no fluxo' },
      {
        kind: 'p',
        text: 'A atuação do Solicitante acontece em **dois momentos**: na **abertura** da requisição e, depois da triagem, no **complemento financeiro**. Entre eles atua a Controladoria (triagem); no fim, o Aprovador, o Financeiro e a Contabilidade.',
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Os dois momentos',
        text: '**1) Abertura** — criação da requisição. **2) Complemento financeiro** — depois da triagem, detalhamento das parcelas e anexo da nota fiscal.',
      },

      { kind: 'heading', text: 'Passo 1 — Abrir a requisição' },
      {
        kind: 'p',
        text: 'Em **Orçamento → Minhas requisições**, o botão **Nova requisição** abre o formulário. Campos a informar:',
      },
      {
        kind: 'list',
        ordered: true,
        items: [
          '**Tipo**: Compra, Prestação de Serviço, Conta Fixa ou Projeto.',
          '**Unidade**, **natureza orçamentária** e **competência** (o mês de referência).',
          '**Fornecedor** — busca pelo CNPJ (o sistema preenche os dados) ou cadastro na hora.',
          '**Valor estimado** e **data do primeiro vencimento**.',
          '**Documentos de apoio** (contrato, orçamento, proposta).',
          'Envio para a **triagem**.',
        ],
      },
      {
        kind: 'callout',
        tone: 'success',
        title: 'Descrição do pedido',
        text: 'Quanto mais claro o pedido — **o que** é, **quanto**, **para qual setor** e **por quê** —, menos idas e voltas na triagem.',
      },

      { kind: 'heading', text: 'Passo 2 — Complemento financeiro' },
      {
        kind: 'p',
        text: 'Aprovada a triagem pela Controladoria, a requisição retorna para o detalhamento do pagamento:',
      },
      {
        kind: 'list',
        ordered: true,
        items: [
          'Montar o **plano de pagamento**: cada parcela com **vencimento**, **valor** e **forma** (PIX, boleto ou transferência).',
          'Informar a **data de emissão** do documento.',
          'Anexar a **nota fiscal** (ou o boleto).',
          'Enviar para a **aprovação**.',
        ],
      },
      {
        kind: 'callout',
        tone: 'warning',
        title: 'Atenção',
        text: 'Se a soma das parcelas ficar **acima** do valor aprovado na triagem, ou se o **fornecedor** for trocado, a requisição volta para a triagem. Sem a nota disponível, a parcela fica **pendente de documento** e a nota é anexada depois.',
      },

      { kind: 'heading', text: 'Acompanhamento' },
      {
        kind: 'p',
        text: 'Após o envio, o acompanhamento é feito em **Minhas requisições**. O **sino** (notificações) avisa quando a requisição é **devolvida** para correção, **aprovada** ou **cancelada**.',
      },
      {
        kind: 'table',
        head: ['Status', 'O que significa'],
        rows: [
          ['Complemento', 'Devolvida — corrigir o que a Controladoria apontou e reenviar.'],
          ['Complemento financeiro', 'Passou na triagem — detalhar o pagamento e anexar a nota.'],
          ['Aprovação', 'Aguardando o aprovador — sem ação do solicitante.'],
          ['Em pagamento', 'Aprovada e a caminho do pagamento.'],
          ['Paga / Finalizada', 'Pagamento efetuado e pedido concluído.'],
          ['Cancelada', 'Encerrada sem seguir o fluxo.'],
        ],
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Escopo',
        text: 'O Solicitante enxerga apenas as **unidades e naturezas orçamentárias** do próprio cadastro. Abrir em outra unidade exige contato com um administrador.',
      },
    ],
  },

  // ------------------------------------------------------- Guia da Controladoria (todos os cargos)
  {
    id: 'guia-controladoria',
    navLabel: 'Guia da Controladoria',
    navSub: 'Triagem e aprovações',
    icon: <ClipboardCheck />,
    title: 'Guia da Controladoria',
    sub: 'Triagem das requisições e aprovações',
    blocks: [
      {
        kind: 'p',
        text: 'A **Controladoria** faz a **triagem** das requisições — a primeira conferência antes de seguirem no fluxo — e participa das aprovações de contas a pagar e de adequações orçamentárias.',
      },
      { kind: 'heading', text: 'Atuação no fluxo' },
      {
        kind: 'p',
        text: 'A triagem é a **segunda etapa**: toda requisição enviada pelo Solicitante passa pela Controladoria antes de voltar para o complemento financeiro e seguir para a aprovação.',
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Papel na esteira',
        text: 'A Controladoria **confere e encaminha**, mas **não dá a aprovação final** (isso é do Aprovador) e **não sobrepõe o teto** do orçamento — um estouro se resolve por Adequação.',
      },

      { kind: 'heading', text: 'Passo 1 — Triagem da requisição' },
      {
        kind: 'p',
        text: 'A fila de triagem fica em **Orçamento → Triagem**, tela inicial da Controladoria. Para cada requisição:',
      },
      {
        kind: 'list',
        ordered: true,
        items: [
          'Conferir **fornecedor**, **unidade**, **natureza orçamentária**, **valores** e **documentos**.',
          '**Encaminhar** para o complemento financeiro quando estiver correto.',
          '**Devolver** ao Solicitante quando houver pendências, com o motivo.',
        ],
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Prazo (SLA)',
        text: 'A triagem tem prazo de **24 horas úteis**, contado a partir da entrada da requisição na fila (apenas dias úteis).',
      },

      { kind: 'heading', text: 'Fornecedor na triagem' },
      {
        kind: 'p',
        text: 'O **fornecedor** é conferido na triagem. Dados incorretos podem ser **corrigidos ali mesmo**, sem precisar devolver a requisição só por isso. A tela também mostra as **retenções previstas** para o fornecedor e sinaliza **fornecedor novo**.',
      },

      { kind: 'heading', text: 'Passo 2 — Aprovações' },
      {
        kind: 'p',
        text: 'Além da triagem, a Controladoria participa das aprovações:',
      },
      {
        kind: 'list',
        items: [
          '**Contas a pagar avulsas** — em **Financeiro → Pendências**.',
          '**Adequações orçamentárias** — em **Orçamento → Adequações**; a aprovação depende de uma permissão liberada individualmente por um administrador.',
        ],
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Visão consolidada',
        text: 'A Controladoria enxerga **todas as empresas** (visão consolidada), diferente do Solicitante e do Aprovador, limitados por unidade e natureza orçamentária.',
      },

      { kind: 'heading', text: 'Onde acompanhar' },
      {
        kind: 'list',
        items: [
          '**Orçamento → Triagem** — a fila de triagem.',
          '**Orçamento → Dashboard** e **Orçamentos** — acompanhamento das requisições e do orçamento.',
          '**Financeiro → Pendências** — aprovação de contas a pagar e pendências de nota fiscal.',
          '**Orçamento → Adequações** — as adequações orçamentárias.',
        ],
      },
    ],
  },

  // ------------------------------------------------------- Guia do Contábil (todos os cargos)
  {
    id: 'guia-contabil',
    navLabel: 'Guia do Contábil',
    navSub: 'Classificar e contabilizar',
    icon: <BookText />,
    title: 'Guia do Contábil',
    sub: 'Classificar, registrar a NF e contabilizar',
    blocks: [
      {
        kind: 'p',
        text: 'O **Contábil** classifica e contabiliza as despesas que chegam do Financeiro, registra a **Nota Fiscal** e cuida das **retenções**. As requisições são apenas consultadas — sem aprovar nem pagar.',
      },
      { kind: 'heading', text: 'Ponto de partida' },
      {
        kind: 'p',
        text: 'A tela principal é **Contábil → Revisão de Contabilidade**. Ali ficam os títulos aguardando classificação e contabilização, além dos adiantamentos e dos lançamentos pendentes.',
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Posição no fluxo',
        text: 'A contabilização é a **última** etapa antes de a requisição ser **finalizada** — depois da despesa classificada e da Nota Fiscal anexada.',
      },

      { kind: 'heading', text: 'Passo 1 — Classificar a despesa' },
      {
        kind: 'p',
        text: 'Cada título traz uma **sugestão de conta contábil** (da IA). Na revisão de classificação:',
      },
      {
        kind: 'list',
        ordered: true,
        items: [
          'Conferir a sugestão e **confirmar ou trocar** a conta do **Plano de Contas**.',
          'Ajustar a **natureza orçamentária** quando necessário.',
          'Confirmar para o título seguir para a contabilização.',
        ],
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Fila "Precisa de você"',
        text: 'Títulos com **confiança alta** já vêm pré-classificados; os de **confiança baixa** ficam na fila **"Precisa de você"** para revisão manual.',
      },

      { kind: 'heading', text: 'Passo 2 — Registrar a Nota Fiscal' },
      {
        kind: 'p',
        text: 'A contabilização exige a **Nota Fiscal**. Na aba **Despesas**:',
      },
      {
        kind: 'list',
        items: [
          '**Registrar despesa** para conferir e anexar a NF.',
          'Sem a nota, o título fica em **"NF pendente"** — a ação **Solicitar NF** pede a nota ao solicitante.',
          '**Adiantamentos** (sem NF) seguem por um caminho próprio, na aba **Adiantamentos**.',
        ],
      },
      {
        kind: 'callout',
        tone: 'warning',
        title: 'Sem NF não contabiliza',
        text: 'Fora os adiantamentos, um título **só contabiliza com a Nota Fiscal**; sem ela, fica pendente até a nota chegar.',
      },

      { kind: 'heading', text: 'Passo 3 — Contabilizar' },
      {
        kind: 'p',
        text: 'Com a NF e a classificação prontas, a contabilização gera o lançamento no **Livro Diário** sob regime de **competência**.',
      },
      {
        kind: 'list',
        items: [
          'As **retenções** são calculadas conforme o momento: IRRF, INSS e ISS na **emissão**; PIS/COFINS/CSLL na **baixa** (rateadas).',
          'Lançamentos automáticos sem conta contábil mapeada aguardam a classificação do título na aba **Revisão de classificação** — ao classificar, o lançamento é gerado automaticamente.',
          'Contabilizados todos os títulos de uma requisição, ela passa a **Finalizada** automaticamente.',
        ],
      },

      { kind: 'heading', text: 'Onde consultar' },
      {
        kind: 'p',
        text: 'No menu **Contábil** ficam os livros e demonstrativos:',
      },
      {
        kind: 'list',
        items: [
          '**Livro Diário** e **Livro Razão** — os lançamentos.',
          '**Balancete**, **DRE** e **Balanço Patrimonial** — os demonstrativos por período.',
          '**Retenções** e **Obrigações acessórias** — o fiscal.',
          '**Plano de Contas** — o cadastro das contas contábeis.',
        ],
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Só leitura das requisições',
        text: 'As requisições são **apenas consultadas** (para entender a origem do título); aprovar e pagar são de outras áreas (Aprovador e Financeiro).',
      },
    ],
  },

  // ------------------------------------------------------- Financeiro: Remessa e Retorno (restrito)
  {
    id: 'financeiro-remessa-retorno',
    navLabel: 'Remessa e Retorno',
    navSub: 'CNAB: envio ao banco e baixa',
    icon: <ArrowLeftRight />,
    title: 'Remessa e Retorno — Financeiro',
    sub: 'As telas de CNAB da Gestão Financeira: enviar pagamentos ao banco e dar baixa pelo retorno',
    gated: 'financeManual',
    blocks: [
      {
        kind: 'p',
        text: 'Esta seção é para o time Financeiro. Explica as duas telas de CNAB dentro da **Gestão Financeira** (menu **Financeiro**): a **Remessa** (envio das ordens de pagamento ao banco) e o **Retorno** (baixa dos pagamentos a partir do arquivo que o banco devolve).',
      },
      { kind: 'heading', text: 'Onde encontrar' },
      {
        kind: 'p',
        text: 'Abra **Financeiro → Gestão Financeira**. No topo ficam as abas **Fila de Pagamentos**, **Remessas**, **Retorno CNAB** e **Histórico**.',
      },
      {
        kind: 'screen',
        variant: 'tabs',
        active: 'queue',
        caption: 'As abas no topo da Gestão Financeira.',
      },
      {
        kind: 'callout',
        tone: 'warning',
        title: 'Contas elegíveis e visão por empresa',
        text: 'Só contas do banco **Santander (033)** com **código de convênio** cadastrado entram no CNAB 240 — contas de outros bancos não aparecem na hora de gerar a remessa. As abas **Remessas** e **Retorno CNAB** são **por empresa**: somem na visão consolidada "Todas as empresas" (selecione uma empresa para usá-las).',
      },

      {
        kind: 'screen',
        variant: 'selectCompany',
        caption: 'Sem uma empresa selecionada, as abas não aparecem — o sistema pede para escolher a empresa no topo.',
      },

      { kind: 'heading', text: 'Remessa — enviar os pagamentos ao banco' },
      {
        kind: 'p',
        text: 'A Remessa agrupa vários pagamentos agendados em um **lote** e gera o arquivo **CNAB 240** para enviar ao banco. Há duas formas de montar o lote:',
      },
      {
        kind: 'screen',
        variant: 'tabs',
        active: 'remittances',
        caption: 'Aba Remessas selecionada.',
      },
      {
        kind: 'list',
        items: [
          '**Pela Fila de pagamento:** marque os títulos desejados e use **Gerar remessa** na barra de seleção — o lote sai exatamente com o que você marcou.',
          '**Pela aba Remessas:** clique em **Gerar remessa** e informe a **conta pagadora** e, se quiser, uma **data de corte de vencimento** (inclui os títulos que vencem até essa data).',
        ],
      },
      {
        kind: 'p',
        text: 'Cada remessa aparece na lista com **número (sequência)**, **data**, **quantidade de linhas**, **valor total** e **situação**. Nas remessas com situação **Gerada** você pode:',
      },
      {
        kind: 'list',
        items: [
          '**Baixar** o arquivo CNAB 240 e enviá-lo ao banco (o nome do arquivo já vem no padrão do banco).',
          'Abrir as **Linhas** para ver os títulos do lote. Em cada linha ainda pendente, a única ação é **Retirar da remessa** (com um motivo).',
        ],
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'A baixa não acontece aqui',
        text: 'Gerar e baixar a remessa **não** quita os títulos. A baixa (marcar como Pago) acontece só no **Retorno CNAB**, quando você sobe o arquivo que o banco devolve.',
      },
      {
        kind: 'table',
        head: ['Situação da remessa', 'O que significa'],
        rows: [
          ['Gerada', 'Arquivo pronto para enviar (ou já enviado) ao banco.'],
          ['Confirmada', 'Todos os títulos do lote foram baixados pelo retorno.'],
          ['Parcialmente retornada', 'Parte dos títulos já foi baixada; o resto continua pendente.'],
          ['Cancelada', 'Lote encerrado sem baixa.'],
        ],
      },

      { kind: 'heading', text: 'Retorno — dar baixa pelo arquivo do banco' },
      {
        kind: 'p',
        text: 'Depois de processar a remessa, o banco devolve um **arquivo de retorno (.RET)**. Na aba **Retorno CNAB** você sobe esse arquivo e o sistema concilia os pagamentos.',
      },
      {
        kind: 'screen',
        variant: 'tabs',
        active: 'cnabReturn',
        caption: 'Aba Retorno CNAB selecionada.',
      },
      {
        kind: 'list',
        ordered: true,
        items: [
          'Escolha a **conta pagadora** (a mesma que gerou a remessa).',
          'Envie o arquivo de retorno (aceita **.RET**, .txt ou .csv).',
          'No envio, o sistema **já dá baixa automática** nos títulos que o banco marcou como **Pagos** — aparece um aviso com quantos foram baixados.',
          'Confira a tabela e trate manualmente as linhas que sobraram (rejeitadas, divergentes, etc.).',
        ],
      },
      {
        kind: 'p',
        text: 'Os contadores no topo somam cada resultado, e cada linha recebe uma **classificação**:',
      },
      {
        kind: 'table',
        head: ['Classificação', 'O que significa', 'O que fazer'],
        rows: [
          ['Pago', 'O banco confirmou o pagamento.', 'Já baixado automaticamente no envio.'],
          ['Rejeitado', 'O banco recusou o pagamento.', '"Retirar" para devolver o título e reprogramar na fila.'],
          [
            'Divergente',
            'Pago com valor ou dados diferentes do enviado.',
            'Conferir e decidir: confirmar pago ou retirar.',
          ],
          ['Ambíguo', 'O retorno casou com mais de um título.', 'Conferir qual é o correto antes de confirmar.'],
          ['Não encontrado', 'Não casou com nenhum título em aberto.', 'Verificar a conta pagadora e o arquivo.'],
          ['Já processado', 'Título já baixado em um envio anterior.', 'Nenhuma ação — apenas informativo.'],
        ],
      },
      {
        kind: 'p',
        text: 'Nas linhas que permitem ação, use **Confirmar pago** (dá baixa no título) ou **Retirar** (devolve o título para reprogramar).',
      },
      {
        kind: 'callout',
        tone: 'danger',
        title: 'Arquivo inválido (trailer)',
        text: 'Se o **rodapé (trailer)** do arquivo estiver inconsistente, o sistema mostra um aviso e **bloqueia as ações** até você subir um arquivo válido — isso evita baixas a partir de um retorno corrompido.',
      },
      {
        kind: 'callout',
        tone: 'info',
        title: 'Quem pode agir',
        text: 'Para **subir arquivos** e **confirmar/retirar** títulos é preciso ter acesso de escrita no Financeiro. Quem tem só leitura vê a conferência, mas os botões ficam desabilitados.',
      },

      { kind: 'heading', text: 'Boas práticas' },
      {
        kind: 'list',
        items: [
          'Use no retorno a **mesma conta pagadora** que gerou a remessa.',
          'Confira os **contadores** (pagos, rejeitados, divergentes) antes de encerrar o retorno.',
          'Reprograme na **Fila de pagamento** os títulos que foram retirados ou rejeitados.',
        ],
      },
    ],
  },
];

// Default language bundle (pt-BR). Keys are in English; rendered text is pt-BR.
// Adding another language = a sibling file with the same key shape.
const ptBR = {
  common: {
    appName: 'ULBRA One',
    enter: 'Entrar',
    back: 'Voltar',
    soon: 'Em breve',
  },

  nav: {
    sections: {
      finance: 'Financeiro',
      registry: 'Cadastros',
      structure: 'Estrutura',
      system: 'Sistema',
      modules: 'Módulos',
    },
    home: 'Início',
    accountsPayable: 'Contas a pagar',
    accountsReceivable: 'Contas a receber',
    cashFlow: 'Fluxo de caixa',
    approvals: 'Aprovações',
    costCenters: 'Centros de custo',
    reports: 'Relatórios',
    documents: 'Documentos',
    projections: 'Projeções',
    audit: 'Auditoria',
    vendors: 'Fornecedores',
    customers: 'Clientes',
    employees: 'Colaboradores',
    companiesUnits: 'Empresas e unidades',
    integrations: 'Integrações',
    settings: 'Configurações',
    hr: 'RH',
    accounting: 'Contábil',
    supplies: 'Suprimentos',
    search: 'Buscar…',
    logout: 'Sair',
  },

  landing: {
    nav: {
      features: 'Recursos',
      modules: 'Módulos',
      product: 'Produto',
    },
    hero: {
      eyebrow: 'ERP da ULBRA',
      title: 'A gestão da ULBRA, finalmente num lugar só.',
      subtitle:
        'Financeiro, cadastros e centros de custo num ERP moderno, rápido e feito pra quem opera o dia a dia da instituição.',
      ctaPrimary: 'Entrar no painel',
      ctaSecondary: 'Conhecer os recursos',
      trust: 'Construído internamente pela equipe de Tecnologia & Inovação.',
    },
    mock: {
      balance: 'Saldo operacional',
      payable: 'A pagar (mês)',
      receivable: 'A receber (mês)',
      pending: 'Aprovações pendentes',
      budget: 'Orçamento por centro de custo',
    },
    features: {
      title: 'Tudo que o financeiro precisa — sem planilha.',
      subtitle: 'Da entrada do lançamento ao pagamento, com rastro e aprovação.',
      finance: {
        title: 'Contas a pagar e receber',
        body: 'Lançamentos classificados por empresa, unidade e centro de custo, com auto-aprovação por limite.',
      },
      registry: {
        title: 'Cadastros completos',
        body: 'Fornecedores, clientes e colaboradores com validação e preenchimento automático por CNPJ e CEP.',
      },
      costCenters: {
        title: 'Centros de custo',
        body: 'Orçamento anual e execução por área, transversal às unidades do grupo.',
      },
      approvals: {
        title: 'Aprovações com rastro',
        body: 'Fluxo claro: pendente → aprovado → pago, com auditoria de quem fez o quê.',
      },
    },
    showcase: {
      eyebrow: 'Feito pra operar',
      title: 'Pensado pra quem usa todo dia.',
      body: 'Multi-empresa desde o início, feedback imediato em cada ação e uma interface que não te faz pensar.',
      points: {
        multiCompany: 'Multi-empresa (vários CNPJs do grupo)',
        realtime: 'Feedback imediato e estados de carregamento',
        audit: 'Trilha de auditoria automática em cada registro',
      },
    },
    modules: {
      title: 'Um ERP, vários módulos.',
      subtitle: 'Começando pelo Financeiro. Os próximos já estão no mapa.',
      available: 'Disponível',
    },
    cta: {
      title: 'Pronto pra começar?',
      subtitle: 'Acesse o painel com a sua conta institucional.',
      button: 'Entrar no ULBRA One',
    },
    footer: {
      tagline: 'O ERP moderno da ULBRA.',
      rights: '© {{year}} ULBRA — Universidade Luterana do Brasil. Uso interno.',
    },
  },

  login: {
    brandTagline: 'A gestão da ULBRA num lugar só.',
    title: 'Entrar no ULBRA One',
    subtitle: 'Acesse o painel da instituição.',
    emailLabel: 'E-mail',
    emailPlaceholder: 'voce@ulbra.br',
    nameLabel: 'Nome',
    nameOptional: '(opcional)',
    namePlaceholder: 'Seu nome',
    submit: 'Entrar',
    submitting: 'Entrando…',
    google: 'Entrar com Google',
    orDivider: 'ou',
    devNote: 'Ambiente de desenvolvimento — login simplificado por e-mail.',
    error: 'Não foi possível entrar. Verifique os dados e tente de novo.',
    backToSite: 'Voltar para o site',
  },

  errors: {
    generic: 'Algo deu errado. Tente novamente em instantes.',
    codes: {
      'Company.NotFound': 'Empresa não encontrada.',
      'CostCenter.UnitsInvalid': 'Uma ou mais unidades não pertencem a esta empresa.',
      'Transaction.CostCenterInvalid': 'Centro de custo inválido para esta empresa.',
      'Transaction.UnitInvalid': 'Unidade inválida para esta empresa.',
      'Transaction.AmountInvalid': 'O valor deve ser maior que zero.',
      'Vendor.NotFound': 'Fornecedor não encontrado.',
      'Customer.NotFound': 'Cliente não encontrado.',
      'Employee.NotFound': 'Colaborador não encontrado.',
      'Document.Invalid': 'Documento (CPF/CNPJ) inválido.',
      'Document.CpfInvalid': 'CPF inválido.',
      'Auth.GoogleNotConfigured': 'Login com Google não está configurado.',
      'Auth.GoogleTokenInvalid': 'Token do Google inválido.',
      'Auth.EmailNotVerified': 'E-mail não verificado pelo Google.',
      'Auth.DomainNotAllowed': 'Domínio não autorizado.',
    },
    boundary: {
      title: 'Algo deu errado',
      message:
        'Encontramos um problema inesperado ao exibir esta página. Você pode tentar novamente ou voltar ao início.',
      retry: 'Tentar novamente',
      home: 'Voltar ao início',
    },
  },
} as const;

export default ptBR;

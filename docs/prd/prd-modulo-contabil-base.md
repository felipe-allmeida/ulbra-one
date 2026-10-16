# PRD — Módulo Contábil Base (Diário, Razão, Balancete, DRE)

- **Projeto:** ulbra.one — módulo `Accounting` (schema `accounting`, domínio de navegação "Contábil")
- **Versão:** 0.4 (reconciliada com a entrega dos 4 módulos — AEL-724/725/726/727)
- **Data:** 2026-07-20 (v0.3 em 2026-07-14)
- **Autor:** [AUTHOR]
- **Status:** Rascunho reconciliado. **Os 4 módulos do roadmap (§13) foram entregues**: Diário (AEL-724 + AEL-760), Razão (AEL-725), Balancete por período (AEL-726) e DRE estruturada + encerramento anual (AEL-727). Este PRD passa a ser **registro do que foi construído** + backlog residual (fechamento mensal com checklist, balancete de encerramento, itens opcionais #8–#11).

> **Changelog v0.4 (2026-07-20).** Marcados como entregues: Livro Diário (§6.2), Livro Razão (§7), Balancete por período (§8), DRE em cascata (§9) e encerramento anual (§10.3, novo). Períodos contábeis + bloqueio (§10.1) entregues na AEL-724. As subseções "já existe / a construir" preservam a análise original como registro; o estado atual está anotado 🟢 em cada uma e na tabela do §1.

> **Como ler este documento.** A v0.2 foi escrita sem conhecimento do que já existe no ulbra.one e usava nomes genéricos (`journal_entry`, `conta_id`, `linha_dre`…). Esta v0.3 mantém a intenção e as regras de negócio, mas:
> 1. mapeia cada termo genérico para o **símbolo real em C#/DB** (§2);
> 2. marca cada módulo como **PRONTO / PARCIAL / AUSENTE** com base no código (§1);
> 3. resolve as três perguntas residuais `[?]` com evidência do código (§4);
> 4. converte os "próximos passos" num **backlog de issues concretas** (§13).
>
> Convenção de código do repositório: **identificadores em inglês**, colunas snake_case via `UseSnakeCaseNamingConvention()`. Toda string em pt-BR do PRD é apenas UI/DB — o modelo C# é inglês.

---

## 1. Estado atual da implementação (headline)

O módulo `Accounting` **já existe e está substancialmente construído**: diário de partida dobrada com ciclo de vida `Draft → Posted → Reversed`, plano de contas global, três calculadoras de relatório puras, integração com Contas a Pagar por eventos (MassTransit + outbox) e ~70% das telas da área "Contábil". O que falta é sobretudo **períodos/fechamento**, **estrutura de DRE**, os **livros de visualização (Diário e Razão)** e **enriquecimento do balancete**.

> **Distinção esclarecida (MT, jul/2026): "lançamento" ≠ "Livro Diário".** O **lançamento contábil** é a etapa de *entrada de dados* (criar/editar/contabilizar/estornar) — já existe como tela "Lançamentos contábeis". O **Livro Diário** é o *livro de visualização* cronológico, read-only — o artefato que o contador consulta/exporta — e **ainda não existe** como tela própria. São entradas separadas na tabela abaixo. Mesma lógica vale para o Razão (que também é livro de visualização).

| Módulo / capacidade do PRD | Estado | Evidência (símbolos reais) |
|---|---|---|
| **Lançamento contábil** (entrada de dados) | 🟢 **PRONTO** | `JournalEntry` + `JournalLine` (`Accounting.Domain/Entities`), ciclo `Draft→Posted→Reversed`, invariantes de partida dobrada em `ValidateStructure()`, `Number` sequencial por empresa atribuído no `Post`. Endpoints `POST/PUT/GET /journal-entries`, `POST /{id}/post`, `POST /{id}/reverse`. Tela `contabil-lancamentos.tsx` (já rotulada **"Lançamentos contábeis"**). |
| **Livro Diário** (livro de visualização cronológico) | 🟢 **PRONTO** (AEL-724 + AEL-760) | Tela read-only sob "Demonstrativos/Livros": aba **Planilha** (grade colunada, principal) + aba **Cronológico** (por dia, partidas D/C expandidas, só `Posted`/`Reversed`). Reusa `GET /journal-entries`. `web/src/pages/livro-diario/`. |
| **Razão (Livro Razão)** | 🟢 **PRONTO** (AEL-725) | `GET /accounting/ledger` (`GetGeneralLedger`, `[AsParameters]`, filtro `Root`/`costCenterId`) + `GeneralLedgerBuilder.Signed` (saldo de abertura + running balance) + export CSV; aba Planilha + busca por conta. `web/src/pages/livro-razao/`. |
| **Balancete (Balancete de Verificação)** | 🟢 **PRONTO** (AEL-726) | `TrialBalanceCalculator(from, to)` com `saldo_anterior` + colunas por período + coluna Código reduzido, filtro por CC, guarda de intervalo inválido, export CSV, header no padrão dos relatórios. `web/src/pages/balancete/` (componentizado). Falta só a **variante "de encerramento"** (checklist). |
| **DRE (Demonstração do Resultado)** | 🟢 **PRONTO** (AEL-727) | Reescrito para **cascata** (`linha_dre`): Receita Bruta→Deduções→Líquida→CMV/CSV→Lucro Bruto→OpEx→Resultado Financeiro→LAIR→(−)IRPJ (−)CSLL→Lucro Líquido, filtro por centro de custo, export CSV. `IncomeStatementCalculator` + `GET /accounting/income-statement`. Tela `contabil-dre.tsx`. Era 🟡 (listas planas `Revenues[]`/`Expenses[]`). |
| **Integração Contas a Pagar** | 🟢 **PRONTO** | Eventos `PayableScheduled`/`PaymentRegistered`/`PaymentReversed`/`PayableCancelled` (Finance) → consumers `PayableScheduledConsumer`/`PaymentRegisteredConsumer`/`AccountingReversalConsumer` → `PayablePostingService` (idempotente por `SourceKey`). Fila de exceção `PendingAccountingPosting`. |
| **Multi-entidade (`entidade_id`)** | 🟢 **PRONTO** (com outro nome) | Escrituração por `JournalEntry.CompanyId` (obrigatório, indexado). `Company` = 1 entidade legal = 1 CNPJ-14 = 1 estabelecimento. Não é "reservado para o futuro": já é obrigatório e vivo. |
| **Centros de custo** | 🟢 **PRONTO** (AEL-724 + AEL-725) | `CostCenter` no Core (`core.cost_center`), CRUD + tela `centros.tsx`, `JournalLine.CostCenterId`. Captura/validação na partida + propagação no lançamento automático de CaP (#6-A, AEL-724); `PostedLine.CostCenterId`/`UnitId` + filtro por CC nos relatórios (#6-B, AEL-725), reusado por Razão/Balancete/DRE. |
| **Balanço Patrimonial** | 🟢 **PRONTO** (além do escopo do PRD) | `BalanceSheetCalculator` + `GET /accounting/balance-sheet`, tela `contabil-balanco.tsx`. Fecha `Ativo = Passivo + PL` via linha sintética de resultado em tempo de leitura. |
| **Períodos contábeis (`aberto`/`em fechamento`/`fechado`)** | 🟢 **PRONTO** (AEL-724) | `AccountingPeriod` (`FiscalYear`/`Month`, status `Open/Closing/Closed`) por `CompanyId` + endpoints List/Close/Reopen + `AccountingPeriodGuard` (assimétrico: manual bloqueia `409`, CaP faz clamp) em todos os caminhos de posting. Migration `20260715161709`. |
| **Fechamento mensal** | 🟡 **PARCIAL** (AEL-724) | Bloqueio/reabertura de período existe (Close/Reopen + guard). **Falta** a rotina orientada (checklist de ajustes, emissão de balancete de encerramento do mês). |
| **Encerramento anual** | 🟢 **PRONTO** (AEL-727) | `POST /accounting/year-end-closing` → `YearEndClosingService`: apuração (zera resultado → conta transitória `2.6.20.05.100`, **adicionada ao seed**) + transferência para Lucros/Prejuízos Acumulados + trava dos 12 meses. `Origin = Closing`, idempotente por `SourceKey`. Ver §10.3. (O `"RESULT"` sintético do Balanço foi mantido — anula-se sozinho pós-encerramento.) |
| **Materialização do Razão (§7.2 da v0.2)** | ⚪ **DECISÃO: não materializar** (AEL-725) | Todos os relatórios são **calculados on-the-fly** por calculadoras puras sobre `PostedLine` (`PostedLinesLoader`). A volumetria não exigiu tabela agregada — decisão de arquitetura, não lacuna. |

**Conclusão de escopo (atualizada v0.4):** os itens que eram "verdadeiramente novos" na v0.3 — períodos + encerramento, os livros de visualização (Diário e Razão), a estrutura de DRE (`linha_dre`), o enriquecimento do balancete e a propagação de centro de custo/competência da CaP — **foram todos entregues** (AEL-724/725/726/727). O **backlog residual** é: fechamento mensal orientado por checklist, variante "balancete de encerramento", e os itens opcionais/condicionais [#8](#issue-8)–[#11](#issue-11) (origem granular além de `Closing`, numeração por exercício sem lacunas, resolução de pendências, log versionado).

---

## 2. Glossário de mapeamento — termo do PRD → símbolo real

Esta é a tabela de tradução obrigatória para escrever as issues. Os nomes da v0.2 **não existem** no código; use a coluna do meio.

| Termo genérico (v0.2) | Símbolo real | Observação |
|---|---|---|
| `journal_entry` | **`JournalEntry`** (agregado raiz) | tabela `accounting.journal_entries`; typed id `JournalEntryId` (`journal_`) |
| `journal_entry_line` | **`JournalLine`** | tabela `accounting.journal_lines`; typed id `JournalLineId` (`jline_`) |
| conta / plano de contas | **`LedgerAccount`** (chart **global**, único para todas as empresas) | typed id `LedgerAccountId` (`ledgacct_`) |
| `conta_id` | **`JournalLine.LedgerAccountId`** | FK real → `LedgerAccount` (`Restrict`) |
| `centro_custo_id` | **`JournalLine.CostCenterId`** (`Guid?`, opcional) | ref. solta ao `CostCenter` do Core; typed id `cost_center_` |
| (dimensão extra, não no PRD) | **`JournalLine.UnitId`** (`Guid?`) | segundo eixo analítico já existente (unidade/campus) |
| `data_competencia` | **`JournalEntry.CompetenceDate`** (`DateOnly`) | **única data de negócio** no lançamento |
| `data_lancamento` | `JournalEntry.PostedAt` (+ `CreatedAt` de auditoria) | não é campo editável pelo usuário |
| `data_documento` | **AUSENTE** no lançamento | vive no `Payable` (Finance), não é copiada |
| `data_vencimento` | **AUSENTE** no lançamento | vive no `Payable`/`Installment` |
| `data_liquidacao` | **AUSENTE** como coluna | liquidação é um **lançamento separado** (settlement), não uma data |
| `origem` (6 valores) | **`JournalEntry.Origin : JournalOrigin`** — só `{ Manual, Automatic }` | os 6 valores do PRD não existem; ver §5.2 |
| `origem_ref` | **`JournalEntry.SourceReference`** (texto) **+ `SourceKey`** (chave de idempotência) | dois campos distintos |
| `numero_sequencial` | **`JournalEntry.Number`** (`int?`) | sequencial **por empresa** (não por exercício), atribuído no `Post` |
| `status` (normal/estornado/retificado) | **`JournalEntry.Status : JournalStatus`** — `{ Draft, Posted, Reversed }` | `Reversed`≈estornado; **`Draft` existe e o PRD não previa**; `retificado` não existe |
| `historico` | **`JournalEntry.Description`** (≤500) + `JournalLine.Description` (≤300, opcional) | |
| natureza D/C da partida | **`JournalLine.Side : EntrySide { Debit, Credit }`** + `Amount` sempre positivo | não há colunas `valor_debito`/`valor_credito`; é uma coluna + lado |
| tipo de conta (Ativo/Passivo/PL/Rec/Desp) | **`LedgerAccount.AccountGroup { Asset, Liability, Equity, Revenue, Expense }`** | única classificação que dirige DRE/BP hoje |
| conta devedora/credora | **`LedgerAccount.NormalBalance { Debit, Credit }`** | armazenado explícito (respeita contas retificadoras) |
| sintética/analítica | **`LedgerAccount.Kind { Synthetic, Analytical }`** + `CanReceiveEntry` | só analítica ativa recebe partida |
| `entidade_id` (multi-entidade) | **`JournalEntry.CompanyId`** (`Guid`, obrigatório) | já é a chave de segregação; ver §4 Q1 |
| `linha_dre` | **`LedgerAccount.DreLine : DreLine?`** (AEL-727) | enum persistido, default por prefixo do código (`DreLineClassifier`) no seed, editável no CRUD; ver §4 Q2 e §9 |
| Razão | **`GeneralLedgerBuilder`** / **`GET /accounting/ledger`** (`GetGeneralLedger`) — AEL-725 | on-the-fly sobre `PostedLinesLoader`; saldo de abertura + running balance; ver §7 |
| Balancete de verificação | `TrialBalanceCalculator(from, to)` / `GET /accounting/trial-balance` — período + `saldo_anterior` (AEL-726) | ver §8 |
| Balancete de encerramento | **AUSENTE** (variante) | períodos já existem; falta a variante em fim de período + checklist |
| DRE | **`IncomeStatementCalculator`** (cascata) / `GET /accounting/income-statement` — AEL-727 | `linha_dre` + subtotais; ver §9 |
| estorno | `JournalEntry.Reverse(...)` + `ReversalOfEntryId`/`ReversedByEntryId` | cria lançamento-espelho já *Posted* |
| retificação | **AUSENTE** | correção é só por estorno |
| fechamento / encerramento | fechamento mensal **AUSENTE**; **encerramento anual PRONTO** (AEL-727, §10.3) | `YearEndClosingService` + `AccountingPeriod.Close()` |
| Apuração de Resultado do Exercício (conta) | **`2.6.20.05.100`** (AEL-727, adicionada ao seed) | Lucros/Prejuízos Acumulados já existiam (`2.6.20.05.020` / `2.6.20.05.060`) |
| fila de pendências (não estava no PRD) | **`PendingAccountingPosting`** | posting automático parkado quando falta conta mapeada |

Campos reais de `LedgerAccount` que o PRD não mencionava: `ReducedCode` (código reduzido), `Natureza` (int 1–5, verbatim da planilha — empiricamente = profundidade da hierarquia), além de `IsActive`.

---

## 3. Contexto e escopo (atualizado)

Mantidos da v0.2: entidade **Ulbra S.A.**, regime **Lucro Real**, **regime de competência**, plano de contas **AELBRA "Modelo de Planos"** já existente, centros de custo já existentes, integração **somente com Contas a Pagar** na v1.

Ajustes de escopo derivados do código:

- **Não é greenfield.** O ciclo Diário→Balancete→DRE já roda. As issues devem ser *diffs* contra o que existe, citando os símbolos da §2.
- **Plano de contas é GLOBAL e único.** Após AEL-684 a coluna `company_id` foi removida de `ledger_accounts`; `code` é unicamente global. O seed AELBRA tem **1120 contas** (era 1119; a AEL-727 adicionou a transitória "Apuração do Resultado do Exercício" `2.6.20.05.100`), 5 raízes: `1` ATIVO, `2` PASSIVO, `3` RECEITA, `4` DESPESA, `5` CUSTO. Raízes 4 e 5 mapeiam ambas para `AccountGroup.Expense`; a separação **CMV/CSV** na DRE passou a usar o novo `DreLine` (`CostOfSales` para a raiz `5`), não o `AccountGroup` (§9.3).
- **Balanço Patrimonial já existe** (fora do escopo declarado da v0.2) e deve ser considerado parte do módulo.
- **Fora de escopo (v1)** — mantém a v0.2: LALUR automatizado, SPED (ECD/ECF), assinatura digital, multimoeda, workflow de aprovação multi-perfil, dimensão "projetos". IR/CSLL entram como **lançamento manual** a partir de apuração externa.

---

## 4. Perguntas residuais `[?]` — resolvidas pelo código

**Q1 — Incluir `entidade_id` no lançamento?** → **Já existe, como `CompanyId`.** Toda `JournalEntry` (e todo `Payable`) carrega `CompanyId` obrigatório, indexado, e ele já dirige a numeração sequencial e a idempotência por empresa. **Não crie um `entidade_id` novo.** `Company` ≡ entidade legal ≡ CNPJ-14 ≡ estabelecimento (modelo "flat", decisão consolidada no EFD-Reinf; **não** existe entidade `Establishment`). Relatórios rodam por empresa **ou consolidados** (`CompanyId == null` ⇒ consolidado). *Ação no PRD: renomear `entidade_id` → `CompanyId` e tratar multi-entidade como dimensão viva e obrigatória, não reservada.*

**Q2 — Existe o atributo `linha_dre` no plano de contas?** → **Não.** `LedgerAccount` tem `AccountGroup` (5 buckets), `NormalBalance`, `Kind`, `Natureza`, `ReducedCode`, `Code`, `ParentId`, `IsActive` — **nenhum** mapeamento conta→linha da DRE. A DRE atual é derivada apenas de `AccountGroup ∈ {Revenue, Expense}`. **Construir a DRE estruturada exige adicionar** um atributo de linha de DRE ao `LedgerAccount` (ou uma tabela de mapeamento à parte). `Natureza` **não** serve para isso (é profundidade da árvore). Ver §9 e Issue #4.

**Q3 — Destinação do lucro em S.A. (reserva legal, dividendos)?** → **Fora da v1, confirmado.** Reserva legal e dividendos ficam para o módulo de Balanço/PL. ~~E nem a transferência básica está construída~~ → **transferência básica ENTREGUE (AEL-727, §10.3):** a conta transitória "Apuração de Resultado do Exercício" (`2.6.20.05.100`) foi adicionada ao seed e o `YearEndClosingService` faz apuração + transferência para Lucros/Prejuízos Acumulados. O `"RESULT"` sintético do Balanço (`PeriodResultAccountId = Guid.Empty`) foi **mantido** (anula-se sozinho pós-encerramento; serve o exercício em curso).

---

## 5. Regras transversais (corrigidas)

### 5.1 Regime de competência
Mantido. O sistema opera por competência; reconhecimento e liquidação são **lançamentos separados** vinculados por conta transitória (ex.: Fornecedores a Pagar). Confirmado no código: a liquidação (`PostSettlementAsync`) é um lançamento distinto do reconhecimento (`PostIncurralAsync`), não uma data no mesmo lançamento.

### 5.2 Datas — a realidade é uma só data
**Divergência importante vs v0.2.** O lançamento carrega **apenas `CompetenceDate`** (+ auditoria `CreatedAt`/`PostedAt`). Não existem `data_documento`, `data_vencimento`, `data_liquidacao` no lançamento — essas datas vivem no `Payable`/`Installment` (Finance). Diário, Razão, Balancete e DRE indexam por `CompetenceDate`.

> ✅ **Resolvido (AEL-724, [Issue #7](#issue-7)):** os eventos de CaP passaram a carregar a competência real do título e os lançamentos automáticos a usam (não mais `CompetenceDate = hoje`). A ressalva abaixo permanece como registro da decisão de manter data única.

Decisão a tomar: manter o modelo de data única (competência) e deixar documento/vencimento/liquidação no domínio Finance, **ou** enriquecer o evento e o lançamento com a competência real do título. Recomendação: enriquecer o **evento** com a competência (mínimo), sem duplicar todas as datas no lançamento.

> **Retenções de pagamento (CSRF).** Cada lançamento continua com uma única data, mas o grupo CSRF (PIS/COFINS/CSLL) é reconhecido no lançamento de **liquidação**, cuja `CompetenceDate` é a **data de pagamento** — não a data do documento. É consequência do §5.1 (liquidação é lançamento separado) e do fato gerador do CSRF ser o pagamento (ver §11.4).

### 5.3 Origem do lançamento — enum binário, não taxonomia de 6 valores
`JournalOrigin` tem **só** `{ Manual, Automatic }`. A v0.2 confundia três eixos distintos que no código são separados:
- **origem** (`JournalOrigin`): quem criou (usuário vs sistema);
- **status/ciclo** (`JournalStatus { Draft, Posted, Reversed }`);
- **tipo de posting da integração** (`PostingKind { Incurral, Settlement, Reversal }`, só no caminho CaP).

A proveniência fina (`contas_a_pagar`, `fechamento`, `encerramento`) hoje é carregada por **strings** em `SourceReference`/`SourceKey` (ex.: `payable-incurral:{PayableId}`, `payment:{PaymentId}`, `…:reversal`). Se o negócio precisar filtrar por essas origens, é preciso **estender `JournalOrigin`** ([Issue #8](#issue-8)) — não assuma que já dá para distinguir.

### 5.4 Numeração sequencial — por empresa, não por exercício
`JournalEntry.Number` é `int?`, atribuído no `Post`, único por `(CompanyId, Number)` (índice filtrado `number IS NOT NULL`). **Não** é por exercício e **não** há garantia forte de ausência de lacunas (o `max+1` é calculado pelo caller `NextNumberAsync`; um posted soft-deleted pode deixar buraco; drafts têm `Number = null`). Se "sequencial por exercício sem lacunas" for requisito fiscal duro, é **trabalho novo**: o índice viraria `(CompanyId, FiscalYear, Number)` e a atribuição precisaria de um contador transacional. Ver Issue #9.

### 5.5 Ciclo de vida do lançamento (novo — o PRD não previa)
O real é `Draft → Posted → Reversed`:
- **Draft**: editável (header + linhas). Pode ser salvo **desbalanceado**; exige apenas ≥2 linhas e valores > 0.
- **Post** (`POST /journal-entries/{id}/post`): valida `ValidateStructure()` (≥2 linhas, valores > 0, Σdébito = Σcrédito), atribui `Number`, seta `PostedAt`, congela. Concorrência tratada com retry (5 tentativas) sobre o índice único.
- **Posted é imutável**; correção só por **estorno** (`Reverse`), que cria um **novo** lançamento-espelho já *Posted*, linka `ReversalOfEntryId`/`ReversedByEntryId` e move o original para `Reversed` (exige `reason`).

O PRD deve adotar esse ciclo. Não existe edição in-place de posted, nem status `retificado`.

### 5.6 Rastreabilidade e auditoria
`IAuditable` (`CreatedAt/By`, `UpdatedAt/By`) + `ISoftDeletable` (`DeletedAt`) já em `JournalEntry`/`LedgerAccount`. **Lacuna vs v0.2:** não há log versionado imutável de alterações campo-a-campo (o módulo `Audit` existe no repo com `AuditContext` próprio; avaliar reutilização). Numeração sequencial sem lacunas — ver §5.4.

### 5.7 Perfil de acesso (v1)
Mantido: perfil único administrador. Endpoints apenas `.RequireAuthorization()`, sem segmentação por cargo. Segmentação fica para quando o módulo de perfis estiver definido.

---

## 6. Módulo 1 — Lançamento contábil + Livro Diário

> **São duas coisas distintas** (esclarecimento MT, jul/2026): o **lançamento contábil** é a etapa de *entrada* dos dados (🟢 já existia); o **Livro Diário** é o *livro de visualização* cronológico e read-only (🟢 entregue na AEL-724, aba Planilha na AEL-760). Não houve rename — a tela de entrada continua "Lançamentos contábeis"; o Livro foi **adicionado** ao lado. É o par do Livro Razão (§7): ambos são livros de leitura sobre o mesmo dado escriturado.

**Diário × Razão — mesma escrituração (`JournalEntry`/`JournalLine` `Posted`), só muda a forma de visualização** (esclarecimento MT, jul/2026):

| Aspecto | **Livro Diário** | **Livro Razão** |
|---|---|---|
| Ordenação | **Cronológica** — na ordem em que as movimentações ocorrem (`CompetenceDate` → `Number`) | **Agrupada por conta** (`LedgerAccount`) |
| Unidade de leitura | O **lançamento**: cada fato com suas partidas de **débito e crédito** e as contas contábeis correspondentes | A **conta**: todas as suas movimentações no intervalo |
| Saldo exibido | Total do dia/período | **Saldo acumulado** (running balance) da conta |
| Pergunta que responde | "O que aconteceu, e em que ordem?" | "Como esta conta se movimentou?" |
| Estado | 🟢 entregue — AEL-724/760 ([Issue #12](#issue-12)) | 🟢 entregue — AEL-725 ([Issue #2](#issue-2)) |

Nenhum dos dois altera dado: são telas **read-only** derivadas dos mesmos lançamentos escriturados.

### 6.1 Lançamento contábil (🟢 PRONTO)
Etapa de entrada de dados — criar, editar, contabilizar, estornar.
- Agregado `JournalEntry` + `JournalLine` com invariantes de partida dobrada (`ValidateStructure`: ≥2 linhas, valores positivos, Σd=Σc), códigos de erro estáveis (`JournalEntry.TooFewLines`, `.NonPositiveAmount`, `.Unbalanced`).
- Validação de conta: só `Analytical` + `IsActive` + não deletada recebe partida (`CanReceiveEntry`), erros `JournalEntry.AccountNotFound` / `.NonPostableAccount`.
- Ciclo `Draft→Posted→Reversed` (§5.5); estorno cria espelho (§5.5).
- Endpoints: `POST /journal-entries`, `PUT /journal-entries/{id}`, `GET /journal-entries/{id}`, `GET /journal-entries` (filtros `companyId`, `status`, `from`, `to` em `CompetenceDate`; ordena por `CompetenceDate` depois `Number`), `POST /{id}/post`, `POST /{id}/reverse`.
- Tela `contabil-lancamentos.tsx` (menu/título **"Lançamentos contábeis"**, subgrupo **Movimento**): editor multi-linha com checagem de saldo ao vivo (Save desabilitado se Σd≠Σc), ações Contabilizar/Estornar, detalhe read-only para posted/reversed.
- Idempotência de lançamentos de sistema via `SourceKey` (índice único `(CompanyId, SourceKey)`).

### 6.2 Livro Diário (🟢 ENTREGUE — AEL-724 + AEL-760)
Livro de **visualização** formal — o que se consulta/exporta, não onde se digita.
- **Entregue:** tela dedicada read-only `web/src/pages/livro-diario/` sob **Demonstrativos/Livros**, com filtro por empresa + intervalo de competência, exibindo apenas escriturados (`Posted` + `Reversed`; sem `Draft`). Backend reusa `GET /journal-entries` (ordena por `CompetenceDate`→`Number`, retorna as linhas).
- **Duas abas (AEL-760):** **Planilha** (grade colunada, aba principal/default) + **Cronológico** (ex-"Livro" — agrupamento por dia com totais, partidas D/C expandidas via `day-group.tsx`/`entry-block.tsx`).
- **Decisão respeitada:** a tela de entrada `contabil-lancamentos.tsx` não foi renomeada; o Livro é tela nova de visualização (par do Razão).
- **Backlog:** termo de abertura/encerramento + export PDF da versão oficial (não implementado).

### 6.3 Divergências vs v0.2 (a corrigir no texto)
- Não há `origem` com 6 valores (§5.3), nem `status` `normal/estornado/retificado` (§5.5), nem múltiplas datas (§5.2), nem numeração por exercício (§5.4).
- `Amount` é sempre positivo; o lado é `EntrySide` — não há colunas separadas de débito/crédito nem valor com sinal.
- Existe dimensão extra `UnitId` na linha, além de `CostCenterId`.

### 6.4 Lacunas do lançamento
- ✅ **Guarda de período fechado** em todos os caminhos de posting — **entregue** ([Issue #1](#issue-1)/AEL-724): `AccountingPeriodGuard` em `CreateJournalEntry`/`PostJournalEntry`/`ReverseJournalEntry` + `PayablePostingService`.
- ✅ **Validação/propagação de `CostCenterId`** na partida — **entregue** ([Issue #6-A](#issue-6)/AEL-724).
- **(residual)** Sem endpoint de exclusão/descarte de draft (se necessário no fluxo manual).
- **(residual)** Sem paginação nem busca textual no `GET /journal-entries`.

### 6.5 Critérios de aceite (revistos)
**Lançamento contábil:**
- Não é possível **postar** lançamento com Σdébito ≠ Σcrédito (draft pode ser salvo desbalanceado; o gate é no `Post`).
- Posted/Reversed são imutáveis; correção só por estorno com `reason`.
- Estorno gera espelho *Posted* com `ReversalOfEntryId`/`ReversedByEntryId` e numeração própria.
- Lançamento de CaP carrega `Origin = Automatic`, `SourceReference`/`SourceKey` preenchidos (ver §11).
- ✅ Postar em competência de período fechado é bloqueado (`409`, manual) / clampado (CaP) — **entregue** ([Issue #1](#issue-1)/AEL-724).

**Livro Diário (visualização):**
- Consulta por empresa + intervalo de competência retorna todos os lançamentos escriturados (`Posted`/`Reversed`) ordenados por `CompetenceDate` depois `Number`, com as partidas D/C expandidas.
- É read-only — nenhuma ação de edição/contabilização/estorno na tela do livro.
- Rascunhos (`Draft`) não aparecem no Livro Diário.
- A soma dos débitos e créditos exibidos no dia bate com o total diário; reemissão de período fechado é idempotente (após [Issue #1](#issue-1)).

---

## 7. Módulo 2 — Livro Razão (🟢 ENTREGUE — AEL-725, PR #86)

### 7.1 Situação
Assim como o Livro Diário (§6.2), o Razão é um **livro de visualização** — a mesma escrituração, reorganizada por conta. Atenção ao *falso amigo*: o item de menu "Plano de Contas" (`nav.ledgerAccounts`) é o **CRUD do chart**, não o Razão.

Base de dados: `PostedLinesLoader.LoadAsync(uow, companyId?, ct, costCenterId?)` projeta `PostedLine(...)` a partir dos lançamentos `Posted`.

### 7.2 Entregue (AEL-725)
- **`GET /accounting/ledger`** (`GetGeneralLedger`, `[AsParameters]`, filtro `Root` por prefixo de código + `costCenterId?`), com irmão de export **`ExportGeneralLedger`/`GeneralLedgerCsv`**.
- Helper puro **`GeneralLedgerBuilder.Signed()`** (sinal por `NormalBalance`): saldo de abertura na data inicial + **running balance** por conta.
- Tela `web/src/pages/livro-razao/` — aba **Planilha** (principal) + busca por conta específica; download CSV. (A coluna "Documento" foi removida na revisão do PR; ver histórico AEL-725.)
- Saldo de abertura: patrimoniais herdam saldo do período anterior; contas de resultado começam em zero após encerramento ([Issue #3](#issue-3)/AEL-727).
- **Decisão de arquitetura mantida:** on-the-fly (reusando `PostedLinesLoader`), **nada materializado**. Materializar só se a volumetria justificar (não foi necessário).
- ✅ `PostedLine` passou a carregar `CostCenterId`/`UnitId` + filtro por CC — [Issue #6-B](#issue-6) entregue nesta fase, reusado por Balancete e DRE.

### 7.3 Critérios de aceite
- Σdébitos/créditos do Razão de uma conta num intervalo = Σ das partidas daquela conta em `JournalLine` no mesmo intervalo.
- Saldo final do Razão numa data = saldo daquela conta no Balancete na mesma data.
- Idempotência histórica: reconsulta de período fechado retorna o mesmo resultado (após [Issue #1](#issue-1)); retificações via estorno geram movimento em nova data, nunca alteram o histórico.

---

## 8. Módulo 3 — Balancete de Verificação (🟢 ENTREGUE — AEL-726, PR #96)

### 8.1 Entregue (AEL-726)
- `TrialBalanceCalculator.Build(lines, from, to)` com recorte por período: colunas **`saldo_anterior`** + movimento do período + **`saldo_final`**; `IsBalanced => TotalDebit == TotalCredit`. `Balance` assinado pela `NormalBalance`.
- Endpoint `GET /accounting/trial-balance` com `from`/`to` + `costCenterId?`; **guarda de intervalo inválido** (`from > to`); fix para não acusar desbalanceio em recorte por CC.
- Export CSV; coluna **"Código reduzido"**; header no padrão dos relatórios (`ReportHead`).
- Tela **componentizada** `web/src/pages/balancete/` (`layout`/`filters`/`table`).

### 8.2 Backlog residual
- **Variante "balancete de encerramento"** (emitida em data de fim de período, com checklist orientativo de ajustes) — não implementada; depende do fechamento mensal orientado (§10.1).
- Mantido o alerta bloqueante quando Σdébito ≠ Σcrédito, apontando a conta em desequilíbrio.

---

## 9. Módulo 4 — DRE (🟢 ENTREGUE — AEL-727, PR #97)

> **Nota (2026-07-20):** as lacunas listadas em §9.2 foram fechadas pela AEL-727 — `LedgerAccount.DreLine` (§4 Q2), cascata com subtotais, separação CMV/CSV, linhas de IRPJ/CSLL e filtro por centro de custo. As subseções abaixo preservam a análise original ("já existia" = antes da AEL-727) como registro histórico; o estado atual é a cascata descrita em §9.3, e o encerramento correlato em §10.3.

### 9.1 Já existia (antes da AEL-727)
`IncomeStatementCalculator.Build(lines, from, to)` filtra `AccountGroup ∈ {Revenue, Expense}` por `CompetenceDate` e retorna `IncomeStatement(From, To, Revenues[], Expenses[], TotalRevenue, TotalExpense)` com `Result => TotalRevenue − TotalExpense`. Endpoint `GET /accounting/income-statement?companyId&from&to`. Tela `contabil-dre.tsx` com duas seções (Receitas/Despesas) + linha de resultado.

### 9.2 Lacunas vs v0.2 (as maiores da DRE)
- **Sem `linha_dre`** no plano de contas (§4 Q2) → sem a cascata Receita Bruta → Deduções → Receita Líquida → CMV/CSV → Lucro Bruto → Despesas Operacionais → Resultado Financeiro → LAIR → (−)IR (−)CSLL → Lucro Líquido.
- **CMV/CSV não separável**: raízes `4` DESPESA e `5` CUSTO caem ambas em `Expense`.
- **Sem linhas de IR/CSLL** (Lucro Real). A provisão entra como lançamento manual; a DRE precisa de linhas para exibi-la.
- Sem filtro por centro de custo (depende de [Issue #6](#issue-6)).

### 9.3 Entregue ([Issue #4](#issue-4)/AEL-727)
- **`LedgerAccount.DreLine : DreLine?`** — enum persistido (coluna `dre_line`, migration `AddLedgerAccountDreLine` com backfill por prefixo), default por código via **`DreLineClassifier`** (função pura reusada por seed + backfill), editável no CRUD do Plano de Contas.
- `IncomeStatementCalculator`/`IncomeStatementResponse` reescritos para a **cascata** — seções por `DreLine` intercaladas com subtotais derivados (Receita Líquida → Lucro Bruto → Resultado Operacional → Resultado Financeiro → LAIR → Lucro Líquido); cada linha "=" é subtotal (não recebe lançamento). Contribuição por conta = `crédito − débito`.
- Separação **CMV/CSV** via `DreLine.CostOfSales` (raiz `5`), linhas de **IRPJ/CSLL** (`DreLine.IncomeTax`/`SocialContribution`, split sob `4.2.35`).
- Filtro por **centro de custo** (reusa #6-B) + **export CSV** (`IncomeStatementCsv`/`ExportIncomeStatement`).
- Respeita `NormalBalance` explícito (contas retificadoras `(-)` já saem com o sinal correto).
- Exclui lançamentos de encerramento (`Origin == Closing`) — ver §10.3.

### 9.4 Critérios de aceite (revistos)
- Lucro/Prejuízo Líquido da DRE do exercício = valor transferido para Lucros/Prejuízos Acumulados no encerramento (após [Issue #3](#issue-3)).
- DRE por competência ignora `data_liquidacao`: receita competência-julho / recebimento-agosto aparece na DRE de julho.
- Provisões de IR/CSLL lançadas manualmente aparecem nas linhas correspondentes (após [Issue #4](#issue-4)).

---

## 10. Fluxos de fechamento (🟡 PARCIAL — períodos + encerramento anual prontos; falta o fechamento mensal orientado)

**Entregue:** `AccountingPeriod` (status `Open/Closing/Closed`) + bloqueio via `AccountingPeriodGuard` (AEL-724, §10.1) e o **encerramento anual completo** (AEL-727, §10.3). **Falta:** a rotina *orientada* de fechamento mensal (checklist) e a variante "balancete de encerramento".

O "Resultado do Exercício" do Balanço segue como *fold* em tempo de leitura (`BalanceSheetCalculator`, `PeriodResultAccountId = Guid.Empty`) — **mantido de propósito**: pós-encerramento ele zera sozinho (resultado já em Lucros Acumulados) e continua fechando `Ativo = Passivo + PL` no exercício em curso (ver §10.3). (O `AssessmentPeriod` do módulo Fiscal é `(Year, Month)` para EFD-Reinf — falso amigo, não reutilizar como período contábil.)

Blocos reutilizados: `CompetenceDate` como eixo de bucketização; o ciclo `Post`/`Reverse` para os lançamentos de encerramento; o padrão `SourceKey` + índice único para a rotina idempotente; `IncomeStatementCalculator` como fonte do número escriturado.

### 10.1 Fechamento mensal (🟡 bloqueio pronto — falta o fluxo orientado)
- ✅ **Entregue (AEL-724):** `AccountingPeriod` por `(CompanyId, FiscalYear, Month)`, endpoints List/Close/Reopen e o `AccountingPeriodGuard` assimétrico (manual → `409`; CaP → clamp ao período aberto).
- **Backlog:** a sequência orientada (ajustes em período aberto → balancete de verificação → revisão → balancete de encerramento + DRE do mês → bloqueio). Contas de resultado **não** zeram no fechamento mensal (só no anual). Hoje o bloqueio é manual (Close/Reopen), sem checklist.

### 10.2 Encerramento anual — sequência-alvo (🟢 entregue na AEL-727; ver §10.3)
1. Ajustes de dezembro; 2. **lançamento manual** da provisão IR/CSLL (apuração externa); 3. DRE anual; 4. **lançamento de apuração**: zerar contas de resultado contra a conta transitória **"Apuração de Resultado do Exercício"** (que **precisa ser adicionada ao seed** — hoje inexiste) e transferir o saldo para **Lucros/Prejuízos Acumulados** (`2.6.20.05.020` / `2.6.20.05.060`, já no seed); 5. balancete de encerramento anual; 6. bloqueio do exercício; 7. abertura do exercício seguinte (resultado zerado, patrimoniais com saldo). Reserva legal/dividendos ficam fora da v1 (§4 Q3).

> **Passos 4/6 entregues (AEL-727, PR #97).** A rotina de apuração + transferência + trava do exercício está implementada — ver **§10.3**. Os passos 2 (provisão IR/CSLL manual), 3 (DRE anual, já entregue no Módulo 4) e 5 (balancete de encerramento) permanecem manuais/dependentes do Módulo 3.

### 10.3 Encerramento anual — implementação entregue (AEL-727 · Módulo 4 [#3](#issue-3)) 🟢

Endpoint `POST /accounting/year-end-closing` (permissão `AccountingPost`) → `YearEndClosingService.CloseYearAsync(companyId, fiscalYear, ct)`. As mutações são estagiadas na UoW; o endpoint faz `SaveChanges` (tudo num único commit).

**Validações / pré-condições** (falha ⇒ `400`, título = código do `Error`):
- `companyId` obrigatório (`YearEndClosing.CompanyRequired`); `fiscalYear >= 1` (`.InvalidYear`).
- Conta transitória **"Apuração do Resultado do Exercício"** (`2.6.20.05.100`, **adicionada ao seed** — antes inexistia, §4 Q3) e **Lucros/Prejuízos Acumulados** (`2.6.20.05.020`) precisam existir e ser **postáveis** (analítica ativa, `CanReceiveEntry`) — senão `.ApuracaoAccountMissing` / `.RetainedEarningsAccountMissing`. Ambos os códigos são configuráveis via `Accounting:*` (`AccountingPostingOptions`).
- **Idempotência**: se já houver lançamento com `SourceKey = year-end-closing:{companyId}:{fiscalYear}:apuracao`, retorna no-op `AlreadyClosed` (não reescritura).

**Fluxo (o que escritura), competência 31/12, `Origin = Closing`:**
1. Calcula o resultado **exatamente como a DRE** (mesmo `PostedLinesLoader` + `IncomeStatementCalculator` no intervalo `[ano-01-01, ano-12-31]`) — garante a invariante §9.4 (Lucro Líquido da DRE = valor transferido).
2. **Lançamento de apuração** (`…:apuracao`): para cada conta de resultado com saldo, posta o lado oposto (`credit − debit`) para zerá-la; a contrapartida na conta Apuração recebe o resultado líquido (crédito se lucro, débito se prejuízo).
3. **Lançamento de transferência** (`…:transfer`): move o resultado da Apuração para Lucros/Prejuízos Acumulados (lucro → D Apuração / C Lucros; prejuízo → C Apuração / D Prejuízos). Usa **uma** conta de Lucros/Prejuízos Acumulados (o sinal do lado resolve lucro vs prejuízo — evita a armadilha de contra-conta).
4. **Trava do exercício**: fecha os 12 meses (`AccountingPeriod.Close()` por mês, criando o período se faltar).

**Efeito nas validações/fluxo do resto do sistema:**
- Novo valor **`JournalOrigin.Closing`** (atende ao que a DRE precisa da [Issue #8](#issue-8), sem estender às demais origens) marca os dois lançamentos.
- **DRE exclui** `Origin == Closing` (`IncomeStatementCalculator`, filtro `inRange`) — senão a apuração zeraria a própria DRE anual. **Balanço inclui** (o resultado precisa migrar ao PL).
- Pós-encerramento, o `AccountingPeriodGuard` (§6.4/[#1](#issue-1)) passa a agir sobre o ano fechado: lançamento **manual** em competência fechada → `409` (`EnsureOpenAsync`); lançamento **automático** (CaP) → **clamp** ao período aberto corrente (assimetria herdada da Issue #1).
- **`"RESULT"` sintético do Balanço foi MANTIDO** (decisão revista vs [#3](#issue-3), que previa removê-lo): pós-encerramento o fold Receita−Despesa do exercício fechado é 0 (resultado já em Lucros Acumulados), então a linha sintética se anula sozinha; no exercício **em curso** (não encerrado) ela continua fechando `Ativo = Passivo + PL`. Removê-la quebraria o exercício aberto.

**Onde a informação fica registrada:**
- *Dados*: `accounting.journal_entry` — dois lançamentos `origin = Closing`, `source_key` = `year-end-closing:{empresa}:{ano}:apuracao|transfer`, `source_reference = "Encerramento {ano}"`; `accounting.accounting_period` — 12 linhas `(fiscal_year, month, status = Closed)`.
- *Código*: `Application/Posting/YearEndClosingService.cs`, `Endpoints/YearEndClosing/YearEndClosing.cs`, `Periods/AccountingPeriodGuard.cs`, `Reports/IncomeStatementCalculator.cs` (exclusão de `Closing`), `Domain/Enums.cs` (`JournalOrigin.Closing`).

---

## 11. Integração Contas a Pagar (🟢 PRONTO — documentar contrato + fechar lacunas)

### 11.1 Contrato real (event-driven, MassTransit + EF outbox)
Eventos publicados pelo Finance (`Finance.Application/Messaging/PayableAccountingEvents.cs`), atômicos com o negócio via outbox:

| Evento | Campos | Publicado em |
|---|---|---|
| `PayableScheduled` | `PayableId, CompanyId, Amount (bruto), Guid? AccountingAccountId, IReadOnlyList<ScheduledWithholding> Withholdings, DateOnly? CompetenceDate, Guid CostCenterId, DocumentNumber?, CounterpartyName?, CounterpartyDocument?, Guid? UnitId` | **dois pontos de emissão**: "Contabilizar" (`CompleteAccounting`, esteira manual) e o auto-agendamento de payable de requisição (`PayableReceivedConsumer`). `Withholdings` carrega **apenas as retenções de emissão** (IRRF/INSS/ISS); o CSRF sai daqui e vai para a baixa. |
| `PaymentRegistered` | `PaymentId, PayableId, CompanyId, Amount, DateOnly PaymentDate, Guid? CashAccountId, IReadOnlyList<SettledWithholding> Withholdings, DocumentNumber?, CounterpartyName?, CounterpartyDocument?, Guid? RequisitionId, string? Competency, decimal? GrossAmount` | registro de pagamento; `Withholdings` = CSRF (PIS/COFINS/CSLL) rateado nesta baixa. `GrossAmount` (AEL-855) = bruto-equivalente da baixa p/ o realizado da requisição fechar contra a estimativa bruta |
| `PaymentReversed` | `PaymentId, PayableId, CompanyId, Guid? RequisitionId` | estorno de pagamento |
| `PayableCancelled` | `PayableId, CompanyId` | cancelamento de título (inclui o cancelamento propagado da requisição, AEL-855) |
| `PayableReclassified` | mesma carga de `PayableScheduled`, com a NOVA conta | Contábil reclassifica um título já agendado (AEL-848); o Accounting reescritura o incurral |
| `ScheduledWithholding` / `SettledWithholding` | `string TaxType, decimal Amount` | (aninhados; `TaxType` é string p/ desacoplar Finance↔Accounting) |

Consumers (`Accounting.Infrastructure/Messaging`): `PayableScheduledConsumer` → `PostIncurralAsync` (`payable-incurral:{PayableId}`); `PaymentRegisteredConsumer` → `PostSettlementAsync` (`payment:{PaymentId}`); `AccountingReversalConsumer` (ambos os eventos de reversão) → `ReverseAsync`.

Lançamentos gerados por `PayablePostingService` (retenções em dois momentos — ver §11.4):
- **Incurral (provisão):** `D` conta de despesa (bruto, resolvida **por Id** de `AccountingAccountId`) / `C` Fornecedores a Pagar (líquido **só das retenções de emissão**) / `C` uma linha por retenção de emissão (IRRF/INSS/ISS).
- **Settlement (pagamento):** `D` Fornecedores a Pagar (`amount + Σ CSRF`) / `C` uma linha por retenção CSRF (PIS/COFINS/CSLL) / `C` Caixa/Banco (`amount`). Sem CSRF → volta a ser o clássico `D` Fornecedores / `C` Caixa.
- Códigos de config (`AccountingModule.cs`, sobrescrevíveis por `Accounting:*`, marcados `🔶 contábil-validate`): Fornecedores `2.2.05.15.080`, Banco `1.2.05.15.015`, retenções Irrf/Pis/Cofins/Csll/Inss/Iss `2.2.05.30.*`. `TaxType = "Other"` (e não mapeados) → `PendingAccountingPosting`.
- Idempotência total por `SourceKey`; estorno reusa a chave com sufixo `:reversal` (a reversão da baixa desfaz também as linhas de CSRF).

### 11.2 `PendingAccountingPosting` (não estava no PRD — documentar)
Fila de **exceção**, não de revisão geral: dispara **só** quando um código de conta não resolve (ausente/inativa/sintética). Campos: `CompanyId, SourceKey, Kind (PostingKind), Amount, Reason ("AccountNotFound:X"), IntendedCodes, CreatedAt, ResolvedAt`. `GET /accounting/pending-postings` (paginado, read-only) + tela `contabil-pendentes.tsx`. **Lacunas:** sem endpoint de resolução (só `MarkResolved` em testes) e **sem guarda de idempotência** na inserção (reentrega duplica pendência).

### 11.3 Lacunas da integração
- ✅ **Centro de custo propagado** ([Issue #6-A](#issue-6)/AEL-724): `PayableScheduled` + `PostIncurralAsync` passaram a carregar/propagar o CC.
- ✅ **Competência real** ([Issue #7](#issue-7)/AEL-724): o evento carrega a competência do título; o lançamento não nasce mais com `CompetenceDate = data do consume`.
- **(residual) Resolução de pendências** ([Issue #10](#issue-10)): ainda falta o fluxo para transformar `PendingAccountingPosting` em lançamento após corrigir o mapeamento, e a guarda de idempotência na inserção.

### 11.4 Momento de retenção — emissão × pagamento

**Regra de negócio.** Cada retenção na fonte é reconhecida na contabilidade no seu **fato gerador**, que difere por tributo:

| Retenção | Fato gerador | Momento contábil |
|---|---|---|
| IRRF, INSS, ISS | crédito/emissão do documento (competência) | **incurral** — mantém a data do documento |
| PIS + COFINS + CSLL (**CSRF**, DARF 5952) | **o pagamento** (Lei 10.833/2003) | **baixa** — rateado por pagamento (Lei 13.137/2015: proporcional a cada parcela, sem soma mensal) |

O líquido pago ao fornecedor não muda (`bruto − todas as retenções`); muda apenas **em qual lançamento** e **em qual data** cada retenção entra no razão.

**No ulbra-one.** A classificação central é `WithholdingTimings.For(WithholdingTaxType)` → `OnIssue` (IRRF/INSS/ISS/Other) ou `OnPayment` (CSRF), em `Finance.Domain` (espelha `RetentionGroups.For`).
- **Incurral:** `CompleteAccounting` publica em `PayableScheduled.Withholdings` **apenas** as de emissão; o incurral credita `Fornecedores a Pagar = bruto − (IRRF+INSS+ISS)`.
- **Baixa:** `PayablePaymentService` rateia o CSRF por pagamento (`WithholdingProration`, running-sum — o pagamento que quita o título absorve o resíduo), persiste em `PaymentWithholding` (fonte da verdade da retenção realizada) e publica em `PaymentRegistered.Withholdings`; `PostSettlementAsync` lança as contas de CSRF na baixa.
- **Apuração/EFD-Reinf:** `WithholdingFactLoader` une as duas fontes — emissão por `IssueDate` (de `PayableWithholding`) e CSRF por `PaymentDate` (de `PaymentWithholding`); o R-4020 usa a data do fato (`WithholdingFact.EventDate`) no `dtFG`.

---

## 12. Exemplo end-to-end (caso de teste — regrounded)

Mantido o cenário da v0.2 (Ulbra S.A., julho/2026), **com duas ressalvas**:

1. **Códigos ilustrativos ≠ chart real.** Os códigos da v0.2 (`6.1.01 Despesa de Aluguel`, `2.1.05 Aluguéis a Pagar`) **não existem** no AELBRA "Modelo de Planos". Ao virar teste de integração, use os códigos reais do seed (ex.: Fornecedores a Pagar `2.2.05.15.080`) ou trate os códigos como *placeholders* explicitamente marcados. Ver memória "deliverables reference the standard, not the sample".
2. **A DRE estruturada e o balancete por período já estão entregues** (Issues #4/#5). O exemplo passa a validar a cascata da DRE e o balancete com `saldo_anterior`; o "balancete de encerramento" (variante) segue pendente (§8.2).

Invariantes a validar em teste (estado atual):
- 🟢 Σd = Σc no Diário (no `Post`) e no `TrialBalance`.
- 🟢 Saldo do Razão da conta X = valor no Balancete de X ([Issue #2](#issue-2)/AEL-725 entregue).
- 🟢 Resultado da DRE do período = variação líquida das contas de resultado (cascata, AEL-727).
- 🟢 Lançamento de CaP carrega `Origin = Automatic` + `SourceReference`/`SourceKey`.
- 🟢 Reemitir balancete/DRE de período fechado retorna o mesmo output ([Issue #1](#issue-1)/AEL-724 entregue).
- 🟢 Retificar em agosto um fato de julho não altera julho — bloqueado pelo `AccountingPeriodGuard` quando julho está fechado ([Issue #1](#issue-1)/AEL-724).
- 🟢 Lucro Líquido da DRE do exercício = valor transferido para Lucros/Prejuízos Acumulados no encerramento ([Issue #3](#issue-3)/AEL-727, §10.3).

---

## 13. Roadmap de desenvolvimento (4 módulos em sequência)

O trabalho é organizado em **4 módulos entregáveis em sequência — Diário → Razão → Balancete → DRE** — absorvendo os itens de suporte. A numeração dos itens (#1–#12) é **identificador estável** (referenciada ao longo do documento); as fases apenas os agrupam. No Linear, sugere-se criar as issues como itens **independentes** (sem épico guarda-chuva — ver memória `linear-no-umbrella-feature-issues`), sob um projeto/label "Contábil v2", usando as fases como marcos/ciclos.

**Dois princípios de agrupamento:** (a) **#1 (Períodos) lidera** — é fundação que destrava o balancete de encerramento (#5) e o encerramento anual (#3); (b) **#6 (Centro de custo) se divide** em captura no lançamento (#6-A, fase 1) e relatório por CC (#6-B, fase 2, reusado por Balancete e DRE).

> As referências **Issue #N** ao longo do documento são links para os itens abaixo.

### Módulo 1 — Diário (fase 1) — 🟢 entregue (AEL-724; aba Planilha AEL-760)
**Objetivo:** entregar o Livro Diário de visualização e garantir a *integridade do que entra nele*.

| Item | Papel na fase / escopo | Estado |
|---|---|---|
| <a id="issue-12" name="issue-12"></a>**#12 — Livro Diário (tela read-only)** | **Núcleo.** Livro cronológico formal (competência→`Number`, partidas D/C expandidas, agrupado por dia, só `Posted`/`Reversed`), sob "Demonstrativos/Livros". Reusa `GET /journal-entries`. Aba Planilha adicionada na AEL-760. | 🟢 entregue |
| <a id="issue-1" name="issue-1"></a>**#1 — Períodos contábeis + bloqueio de período fechado** | Fundação. `AccountingPeriod` (mês/exercício, status `Open/Closing/Closed`) por `CompanyId`; `AccountingPeriodGuard` em `CreateJournalEntry`/`PostJournalEntry`/`ReverseJournalEntry` + `PayablePostingService`. Destrava #5 e #3. | 🟢 entregue |
| <a id="issue-7" name="issue-7"></a>**#7 — Competência real do título (CaP)** | Enriquecer `PayableScheduled`/`PaymentRegistered` com a competência e usá-la em `PostIncurralAsync`/`PostSettlementAsync`. | 🟢 entregue |
| <a id="issue-6" name="issue-6"></a>**#6-A — Captura de centro de custo** | Validar `CostCenterId` na partida + propagar CC no lançamento automático de CaP. *(A metade #6-B vai no Módulo 2.)* | 🟢 entregue |
| <a id="issue-10" name="issue-10"></a>**#10 — Resolução de pendências + idempotência** | Fluxo para reprocessar `PendingAccountingPosting` após corrigir o mapeamento; guarda de idempotência na inserção. | 🔴 residual |
| <a id="issue-9" name="issue-9"></a>**#9 — Numeração por exercício sem lacunas** | Índice `(CompanyId, FiscalYear, Number)` + contador transacional; reset anual; tratar soft-delete. | 🔴 condicional (só se requisito fiscal) |
| <a id="issue-8" name="issue-8"></a>**#8 — Origem granular (`JournalOrigin`)** | Estender o enum. **Parcial:** a AEL-727 adicionou `Closing` (o que a DRE precisava); `contas_a_pagar`/`estorno` seguem em `SourceKey`. | 🟡 parcial |
| <a id="issue-11" name="issue-11"></a>**#11 — Log versionado de alterações** | Histórico imutável campo-a-campo (avaliar reuso do `Audit`/`AuditContext`). | 🔴 opcional |

**Depende de:** — (é a fundação). **Critérios de aceite:** ver §6.5.

### Módulo 2 — Razão (fase 2) — 🟢 entregue (AEL-725, PR #86)

| Item | Papel na fase / escopo | Estado |
|---|---|---|
| <a id="issue-2" name="issue-2"></a>**#2 — Livro Razão** | **Núcleo.** `GET /accounting/ledger` (`GetGeneralLedger`); `GeneralLedgerBuilder.Signed` = saldo de abertura + running balance; export CSV; tela `livro-razao/`. | 🟢 entregue |
| **#6-B — Relatório por centro de custo** | `PostedLine` carrega `CostCenterId`/`UnitId` + filtro por CC. Nasce aqui e é **reusado por Balancete e DRE**. | 🟢 entregue |

**Depende de:** #1 (idempotência histórica das consultas). **Critérios de aceite:** ver §7.3.

### Módulo 3 — Balancete (fase 3) — 🟢 entregue (AEL-726, PR #96)

| Item | Papel na fase / escopo | Estado |
|---|---|---|
| <a id="issue-5" name="issue-5"></a>**#5 — Balancete por período + `saldo_anterior`** | **Núcleo.** `from`/`to` em `GET /accounting/trial-balance`; colunas `saldo_anterior`/movimento/`saldo_final`; coluna Código reduzido; filtro por CC; export CSV. **Variante "de encerramento" (checklist) fica residual** (§8.2). | 🟢 entregue (variante residual) |

**Depende de:** #1 (recorte de período). **Reusa:** filtro por CC (#6-B). **Critérios de aceite:** ver §8.3.

### Módulo 4 — DRE (fase 4) — 🟢 entregue (AEL-727, PR #97)

| Item | Papel na fase / escopo |
|---|---|
| <a id="issue-4" name="issue-4"></a>**#4 — DRE estruturada + `linha_dre`** 🟢 | **Núcleo.** Atributo conta→linha da DRE em `LedgerAccount` (`DreLine?` + seed via `DreLineClassifier` + CRUD); `IncomeStatementCalculator`/DTO reescritos para a cascata (Receita Bruta→…→IR/CSLL→Lucro Líquido); CMV/CSV separados; filtro por CC; export CSV. |
| <a id="issue-3" name="issue-3"></a>**#3 — Encerramento anual** 🟢 | Fecha o ciclo: conta transitória "Apuração de Resultado do Exercício" (`2.6.20.05.100`) no seed; rotina que zera resultado → Apuração → Lucros/Prejuízos Acumulados + trava dos 12 meses (`Origin = Closing`). Invariante *Lucro Líquido da DRE = valor transferido* validada. **Desvio vs plano:** o *hack* `"RESULT"` do Balanço foi **mantido** (anula-se sozinho pós-encerramento; removê-lo quebraria o exercício aberto). Ver §10.3. |

**Depende de:** #1 (períodos) + #4 (número da DRE) → #3. **Reusa:** filtro por CC (#6-B). **Critérios de aceite:** ver §9.4 e §10.2.

### Mapa item → módulo

| Módulo | Núcleo | Absorve | Estado |
|---|---|---|---|
| **1 — Diário** | #12 | #1, #7, #6-A, #10, #9\*, #8\*, #11\* | 🟢 entregue (AEL-724/760) — residuais: #10, #9, #8 (parcial), #11 |
| **2 — Razão** | #2 | #6-B | 🟢 entregue (AEL-725) |
| **3 — Balancete** | #5 | — (usa #1; reusa #6-B) | 🟢 entregue (AEL-726) — variante encerramento residual |
| **4 — DRE** | #4 | #3 | 🟢 entregue (AEL-727) |

`\*` opcional/condicional na fase 1 (permanecem residuais).

**Itens não-issue** (só correções de texto do PRD, sem desenvolvimento): renomear `entidade_id`→`CompanyId`; adotar o ciclo `Draft→Posted→Reversed`; remover a premissa de múltiplas datas no lançamento; remover a premissa de materialização do Razão (confirmado on-the-fly na AEL-725); reconhecer Balanço Patrimonial e Retenções como já entregues; a tela de entrada já se chama "Lançamentos contábeis" (o Livro Diário #12 é tela nova de visualização, não um rename).

**Backlog residual pós-entrega dos 4 módulos:** fechamento mensal orientado por checklist (§10.1); variante "balancete de encerramento" (§8.2); termo de abertura/encerramento + export PDF do Livro Diário (§6.2); [#10](#issue-10) resolução de pendências; [#9](#issue-9) numeração por exercício (condicional); [#8](#issue-8) origem granular além de `Closing`; [#11](#issue-11) log versionado.

---

## 14. Perguntas abertas (que o código não responde)

1. **Numeração** *(ainda aberta)*: "sequencial por exercício sem lacunas" é requisito fiscal **duro** para a Ulbra S.A.? Ainda não decidido — [Issue #9](#issue-9) permanece condicional (numeração hoje é por empresa).
2. ✅ **Datas na integração** *([Issue #7](#issue-7)/AEL-724)*: **decidido — enriquecer o evento com a competência do título.** Implementado.
3. ✅ **Materialização do Razão** *([Issue #2](#issue-2)/AEL-725)*: **decidido — on-the-fly** (reusando `PostedLinesLoader`); a volumetria não exigiu materialização.
4. ✅ **`linha_dre`** *([Issue #4](#issue-4)/AEL-727)*: **decidido — enum simples no chart** (`LedgerAccount.DreLine`), default por prefixo + editável no CRUD.
5. **Origem rica** *(parcialmente decidida — [Issue #8](#issue-8))*: a AEL-727 adicionou só `JournalOrigin.Closing` (necessário à DRE). Distinguir `contas_a_pagar`/`estorno` por enum (vs `SourceKey`) segue **em aberto**.

---

## 15. Referências de código (para as issues)

- Diário: `src/Modules/Accounting/Ulbra.One.Accounting.Domain/Entities/{JournalEntry,JournalLine,LedgerAccount,PendingAccountingPosting}.cs`, `Enums.cs`; `Infrastructure/Database/Configurations/*`.
- Relatórios: `Accounting.Application/Reports/{PostedLine,TrialBalanceCalculator,IncomeStatementCalculator,BalanceSheetCalculator}.cs`; `Endpoints/Reports/PostedLinesLoader.cs`.
- Endpoints/DTOs: `Accounting.Application/AccountingEndpoints.cs`, `Endpoints/**`, `Contracts/**`.
- Integração CaP: `Accounting.Application/Posting/PayablePostingService.cs`; `Accounting.Infrastructure/Messaging/{PayableScheduled,PaymentRegistered,AccountingReversal}Consumer.cs`; `Finance.Application/Messaging/PayableAccountingEvents.cs`; `Accounting.Infrastructure/AccountingModule.cs`.
- Seed do plano: `Accounting.Application/Seeding/{AelbraChart,AelbraAccountRow}.cs` + `Resources/aelbra-plano-contabil-sa.json`.
- Core: `Core.Domain/Entities/{Company,Unit,CostCenter}.cs`.
- Frontend: `web/src/pages/contabil-*.tsx`, `centros.tsx`; `web/src/nav.config.tsx`; hooks em `web/src/queries.ts`.
- Migrations relevantes: `20260622185314_AddAccountingLedger`, `20260624225749_PayableAccountingPosting`, `20260707120048_RemoveFinancialChartAxis` (AEL-678), `20260708121355_ConsolidateGlobalLedger` (AEL-684), `20260709162416_WipeLedgerAccountsForAelbraReseed` (AEL-442).

# Comparativo: Payable × Requisições do Senior (Serviço e Produto)

> Escopo: Requisição de **Serviço** (`F207RES`) e Requisição de **Produto** (`F207EME`).

## Fontes
- **Payable (nosso ERP):** `CreatePayable.Request` em
  `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Payables/CreatePayable/CreatePayable.cs`
  e entidades `Payable` / `Installment` em `…/Finance.Domain/Entities/`.
- **Senior:** telas `F207RES` "Geração Individual de Requisições de Serviço" e `F207EME`
  "Geração Individual de Requisições de Produto", documentadas na página Notion
  *Campos por momento — Entrevista Rafaela (08/06/2026)*.

## O que cada lado representa
- **Payable** = título financeiro a pagar, com 1..N parcelas. Orientado ao *pagamento*.
- **Requisição (Senior)** = documento de origem da compra (de **serviço** ou **produto**),
  com item, quantidade, preço, contabilização e fluxo aprovação → atendimento → compras.
  O Payable é o mesmo modelo nos dois casos: a diferença Serviço × Produto está toda no
  *documento de origem*, não no título a pagar.

São abstrações de níveis diferentes: a Requisição é o *documento de compra*; o Payable é o
*título a pagar* decorrente. O ponto de contato real é pequeno — fornecedor, centro de
custo, número do documento, data e valor.

## Campos de criação do Payable (referência)
| Campo | Tipo | Obrigatório |
|---|---|---|
| `CompanyId` | empresa | Sim |
| `VendorId` | fornecedor | Sim |
| `CostCenterId` | centro de custo | Sim |
| `CategoryId` | categoria de despesa | Não |
| `Origin` | enum (Manual/Purchase/Payroll/SeniorImport) | default Manual |
| `Description` | texto | Sim |
| `DocumentNumber` | texto | Não |
| `IssueDate` | data | Não |
| `Installments[]` (DueDate, Amount) **ou** `Generate` (TotalAmount, Count, FirstDueDate, IntervalMonths) | parcelas | Sim (um dos dois) |
| _derivados_: `CounterpartyName`, `TotalAmount`, `Status` | — | auto |

## Mapeamento — Requisição de Serviço (`F207RES`)
| Senior — Requisição de Serviço (`F207RES`) | Payable / Installment | Situação |
|---|---|---|
| Número Documento (ex. 73.755) | `DocumentNumber` (opcional) | ✅ Equivalente direto |
| Serviço (código + descrição) | `CategoryId` / `Description` (aprox.) | ⚠️ Parcial — não temos catálogo de serviço |
| Centro Custo Aplicação | `CostCenterId` (obrigatório) | ✅ Equivalente direto |
| Conta Financeira (classificação, ex. `76.100`) | classificação: aprox. `CategoryId`; conta de pagamento definida na baixa via `Payment.BankAccountId` | ⚠️ Parcial |
| Conta Contábil (ex. `40.760`) | — | ❌ Ausente |
| Preço Requisição / Quantidade Movimento | `Installment.Amount` (valor consolidado) | ⚠️ Parcial — sem qtd × preço unitário |
| Unidade (`UN — UNIDADE`, unidade de medida) | — | ❌ Ausente (faz parte de qtd × unidade × preço) |
| Complemento / Observação | `Description` (texto único) | ⚠️ Parcial — um só campo de texto |
| Data Emissão | `IssueDate` (opcional) | ✅ Equivalente |
| Data Previsão Entrega (entrega do serviço) | — (≠ `DueDate`, que é vencimento financeiro) | ❌ Ausente |
| Fornecedor (implícito no documento) | `VendorId` + `CounterpartyName` | ✅ Temos (mais forte) |
| Filial | `CompanyId` (obrigatório) | ✅ Equivalente |
| Transação (`90250 …`) | `Origin` (enum) | ⚠️ Conceito diferente |
| Situação (`1 — Digitado`) | `Status` (PendingApproval→Approved→Settled) | ⚠️ Workflows distintos |
| Usuário Solicitante / Aplicação / Última Alteração | `CreatedBy` / `UpdatedBy` | ⚠️ Parcial |
| Bloco aprovação (Qtde Aprovada, Usuário/Data Aprovação) | `Status=Approved` (sem auditoria de quem/quando) | ⚠️ Parcial |
| Bloco atendimento (Qtde Atendida/Cancelada, Usuário Atendente, Qtde p/ Compras) | — | ❌ Ausente (não há fase de atendimento) |
| Número Cotação / Cliente / Bem / Seq. endereço entrega | — | ❌ Ausente (fora do escopo financeiro) |
| — | `Installments[]` / `Generate` (parcelamento) | 🆕 Nosso — Senior parcela no contrato/OC |
| — | `TotalAmount` (soma das parcelas) | 🆕 Nosso (derivado) |

## Mapeamento — Requisição de Produto (`F207EME`)
O Payable não distingue serviço de produto, então **todos os campos comuns mapeiam igual à
tabela de Serviço acima** (Número Documento, Data Emissão, Centro Custo, Conta
Financeira/Contábil, Fornecedor, Filial, Situação, blocos de aprovação/atendimento etc.).
A tabela abaixo lista só o que **muda ou é exclusivo** do Produto.

| Senior — Requisição de Produto (`F207EME`) | Payable / Installment | Situação |
|---|---|---|
| Produto (código + descrição, ex. `120475 — ZOLETIL`) | `CategoryId` / `Description` (aprox.) | ⚠️ Parcial — não temos catálogo de produto |
| Derivação | — | ❌ Ausente (variação/SKU do produto) |
| Unidade (do cadastro, ex. `FRC — FRASCO`) | — | ❌ Ausente (qtd × unidade × preço) |
| Preço Requisição (puxa do cadastro do produto) | `Installment.Amount` (consolidado) | ⚠️ Parcial — sem qtd × preço unitário |
| Endereço de Entrega | — | ❌ Ausente |
| Reserva Estoque (Sim/Não) | — | ❌ Ausente (controle de estoque) |
| Depósito | — | ❌ Ausente (estoque) |
| Filial Pedido | — | ❌ Ausente (≠ `CompanyId`; é a filial do pedido de compra) |
| Número Pedido / Sequência Pedido | — | ❌ Ausente (logística de compras) |
| Número Análise Embarque | — | ❌ Ausente |
| Número Pré-fatura | — | ❌ Ausente |
| Usuário Comprador | — | ❌ Ausente |

**Leitura:** o produto traz uma camada de **estoque/logística de compras** (derivação,
depósito, reserva, pedido, embarque, pré-fatura, comprador) que está ainda mais distante de
um título a pagar do que o caso de serviço. No recorte financeiro, serviço e produto
colapsam no **mesmo** subconjunto: fornecedor, centro de custo, documento, data e valor.
Esses campos extras pertencem a um futuro módulo de **compras/estoque**, não ao Finance/Payable.

> ⚠️ **Pré-requisito de cadastro:** produto novo exige **cadastro prévio pelo Patrimônio
> (Moacir Quadros da Cunha)** antes de poder ser usado na requisição — diferente do serviço.
> Relevante para o desenho de um futuro cadastro de produtos/itens.

## Lacunas que podem importar
1. **Conta Contábil / Conta Financeira (classificação)** — no Senior classificam a despesa
   (plano de contas: financeiro `76.100`, contábil `40.760`). Hoje só temos `CategoryId`
   (opcional). Gap real se a contabilidade exigir plano de contas.
   - ⚠️ Não confundir com a **conta de pagamento**: o banco de onde o dinheiro sai já é
     capturado na baixa via `Payment.BankAccountId`. O Payable é criado sem definir banco
     (decisão adiada para o momento da baixa) — comportamento desejado, não lacuna.
2. **Auditoria de aprovação** — temos `Status=Approved`, mas não registramos aprovador nem
   timestamp; o Senior registra explicitamente.
3. **Quantidade × Preço unitário** — guardamos só `Amount` consolidado por parcela.

## Impostos e retenções (tributário) — ciência para módulos futuros
> Nada disso é necessário para a baixa (AEL-452). Esta seção existe para registrar
> **onde** o tratamento tributário nasce no Senior e **o que** teríamos que modelar quando
> o ERP for assumir esse papel (provavelmente junto do **módulo de Contrato** —
> ver esboço `SupplierContract` / `F460CTR`).

**Onde os impostos aparecem no Senior (pelos prints):**
- **Requisição de Serviço (`F207RES`)** — não tem campos de imposto.
- **Ordem de Compra (`F420GOC`)** — só a aba *Dados Gerais* foi capturada (sem % de
  imposto). ⚠️ A grid de *itens* da OC não foi printada, então não dá pra afirmar que a OC
  não tributa — apenas que não apareceu.
- **Contrato (`F460CTR`) → aba Serviços** — é **aqui** que os impostos são definidos, por
  linha de serviço:
  - **Retenções na fonte:** % IRRF, % INSS, % PIS, % CSLL, % COFINS
  - **Indiretos:** % IPI, % ICMS, % ISS, % Redução ISS
  - **% Desconto / Valor Desconto**

**Por que importa no Brasil:** as retenções na fonte (IRRF, INSS, ISS, PIS/COFINS/CSLL em
NF de serviço) mudam o **valor líquido pago** (sai do banco o bruto − retenções) e geram
obrigações acessórias (guias de recolhimento, **EFD-Reinf**, **DCTFWeb**).

**No nosso modelo hoje:** `Payment` tem `Interest` / `Fine` / `Discount`, mas **não** existe
conceito de **retenção**. Hoje esse cálculo é responsabilidade do Senior/Fiscal (ver páginas
de processo Controladoria/Contabilidade/Fiscal) — por isso está fora do escopo da baixa.

**Quando formos substituir essa parte (Contrato + fiscal):** modelar imposto/retenção
provavelmente em dois pontos —
- nas **linhas de serviço do contrato** (alíquotas previstas, como no `F460CTR`); e
- no **`Payment`** (bruto × retido × líquido), para a baixa refletir o valor efetivamente
  pago e alimentar as obrigações acessórias.

Isso deve ser considerado ao evoluir o esboço `SupplierContract` (que hoje cobre vigência,
parcelas e renovação, mas **não** trata tributos).

## Onde o Payable é mais forte
- Parcelamento nativo (`Installments` / `Generate`).
- Multiempresa explícito (`CompanyId`).
- Módulo de baixa/pagamento (Payment + BankAccount, AEL-452) — no Senior é processo
  separado, fora da requisição.

## Cuidado ao mapear (campos parecidos, significados diferentes)
- `Installment.DueDate` = **vencimento financeiro**.
- "Data Previsão Entrega" (Senior) = **entrega do serviço**. Não confundir.

## Nota de processo
No fluxo real (página *Encaminhamento NF*), o time de Contas a Pagar não cria o Payable
diretamente: cadastra Requisição/OC/Contrato no Senior e abre card no **Pipefy**. O
formulário do Pipefy é o que mais se aproxima da intenção do nosso Payable.

| Pipefy — Criação de Requisição | Payable |
|---|---|
| Número da requisição (Senior) | `DocumentNumber` |
| Origem (OC / Contrato) | `Origin` |
| Código + Nome do Fornecedor | `VendorId` / `CounterpartyName` |
| Data de vencimento | `Installment.DueDate` |
| Forma de pagamento (Boleto/PIX) | `Payment.Method` (na baixa) |
| Setor | ≈ `CostCenterId` |
| Anexar NF / Boleto | `PayableAttachment` |
| Tipo de pagamento (Normal/Adiantamento) | ❌ Ausente |

## TODO
- [x] Mapeamento da Requisição de **Produto** (`F207EME`) — incluído acima.
- [ ] Decidir se Conta Contábil/Financeira entram no Payable.
- [ ] Decidir se registramos auditoria de aprovação (aprovador + data).
- [ ] Tributário/retenções: ao construir o módulo de **Contrato** (`SupplierContract` /
      `F460CTR`), avaliar modelar impostos nas linhas de serviço + retenções no `Payment`
      (bruto × retido × líquido) e obrigações (EFD-Reinf / DCTFWeb). Não é escopo da baixa.
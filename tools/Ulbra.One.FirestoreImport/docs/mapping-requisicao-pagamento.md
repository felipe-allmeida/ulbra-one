# Mapeamento Firestore (orcamento) → Ulbra One — **Requisition + Payable**

Objetivo: migrar cada solicitação legada como **Requisition** (Budgeting) **e** **Payable** (Contas a Pagar / Finance) no One, mantendo **fiel ao que foi orçado** (realizado/empenhado batendo com o consumo real).

> Fonte dos números: apuração ao vivo do Firestore (projeto `spherical-cell-g07pf`, banco nomeado `ai-studio-…`). Estrutura do One: snapshot EF (`AppDbContextModelSnapshot.cs`) — fonte de verdade.

---

## 1. Descoberta-chave: são DUAS collections, e casam com os DOIS agregados

| Legado (Firestore) | Qtd | → One (agregado) | Tabelas |
|---|---|---|---|
| `requisicoes` | **3.338** | **Requisition** (Budgeting) | `budgeting.requisitions` (+ `requisition_installment`, `+ requisition_payment`) |
| `pagamentos` | **28.264** | **Payable** (Finance) | `finance.payables` (+ `installments`, `+ payments`, `+ fiscal_documents`, `+ payable_attachments`) |

- **`pagamentos` é 1 doc por PARCELA** (`id = PAY_<requisitionId>_<seq>`) e carrega o que foi **de fato pago**: valor, `juros`/`multa`, `dueDate`/`paymentDate`, `paymentMethod`, `linhaDigitavel`/PIX, `fiscalDocumentUrl` (NF), `boletoDocumentUrl`, `paymentReceiptUrl` (comprovante), `bankRemittanceId`, `status`.
- **3.185** requisições têm pagamento; **~153** não têm (quase todas `Cancelado`/pré-aprovação) → essas viram **só Requisition, sem Payable**.

### Regra de ouro (fidelidade ao orçado) — vale a pena ler antes das tabelas
O `BudgetControlService` do One **só lê o lado Requisition** (`requisitions` + `requisition_payment`); **nunca lê Payable**. Logo:
1. O **Payable** é o registro operacional de Contas a Pagar — não afeta o orçamento por si.
2. O **realizado** do orçamento sai **exclusivamente** de `budgeting.requisition_payment`.
3. Como escrevo **direto (Dapper, sem outbox)**, **nenhum evento dispara** → tenho que escrever o `requisition_payment` **à mão** a partir dos `pagamentos` pagos. Isso substitui o "pagamento sintético por status" que eu fazia antes → agora o realizado vem do **pagamento real** (competência + valor + data).
4. **Sem dupla contagem**: os dois lados não se cruzam; o orçamento conta o realizado uma única vez (do `requisition_payment`), desde que o `status` da requisição seja coerente com os pagamentos.
5. **Contábil (ledger) NÃO é alimentado** por escrita direta (depende de eventos `PayableScheduled`/`PaymentRegistered`). → **Fora do escopo desta fase** (ver Dúvida #13).

---

## 2. Estrutura dos documentos no Firestore

### 2.1 `requisicoes` (campos usados)
`id, unitCode, unitName, natureName, status, type, estimateType, paymentSystem, estimateValue, startCompetency, endCompetency, providerCnpj, providerName, requesterEmail, requesterName, requesterUid, description, observation, seqNum, paymentFirstDueDate, paymentInstallmentsCount, paymentFrequency, alreadyPaidInstallments, programBeforeApproval, projectId, projectName, attachments[], approvedAt, triagedAt, createdAt, updatedAt, aiTriagemAnalysis{…}`

### 2.2 `pagamentos` (1 por parcela — `PAY_<reqId>_<seq>`)
`id, requisitionId, seqNum, amount, originalAmount, juros, multa, competency, dueDate, paymentDate, paymentMethod, status, returnStatus, authCode, natureName, unitCode, providerCnpj, providerName, linhaDigitavel, pixKey, pixCopyAndPaste, bankControlNumber, bankRemittanceId, bankTransferDetails, fiscalDocumentUrl/Name, boletoDocumentUrl/Name, paymentReceiptUrl/Name, isExternal, importId, importType, title, description, createdAt, updatedAt`

**Distribuições reais (28.264 pagamentos):**
- `status`: **Pago 24.851**, Contabilizado 2.332, Fila_Pagamento 684, Fila_Remessa 230, Cancelado 99, Pendente_Aprovação 57, Devolvido 10, Avaliacao_Imposto 1.
- `paymentMethod`: transferência bancária 24.206, boleto bancário 2.219, chave pix 1.784, pix copia e cola 53, informar posteriormente 2.
- `returnStatus`: Manual 25.278, Baixado 1.905, Aguardando Retorno 253, none/'' 828.
- `isExternal`: **True 24.070 (85%)**, none 4.194.
- `juros`/`multa` presentes: **97** (e são exatamente os 97 em que `amount ≠ originalAmount`).
- Com `fiscalDocumentUrl`: 3.703 · boleto: 2.222 · comprovante: 3.113 · PIX: 2.061.
- Parcelas por requisição: 1 (2.750 reqs), 2 (230), 3 (85), 6 (37)… e **4 requisições com 293 parcelas cada** (baldes de importação externa).
- **Σ amount pago = R$ 170.727.267,18** (vs R$ 260,5 mi orçado).

---

## 3. De-para: `requisicoes` → **Requisition** (Budgeting) — *já implementado*
Resumo (detalhe no importador atual): head `budgeting.requisitions` + `requisition_installment` (plano de parcelas) + `requisition_payment` (realizado). Status legado → `RequisitionStatus` (Finalizada→Finalized, PAGO→Paid, Em Pagamento→InPayment, AGUARDANDO DOCUMENTO FISCAL→AwaitingFiscalDocument, Triagem/Aprovação/Complementação, Cancelado→Cancelled). **Mudança proposta:** o `requisition_payment` passa a ser derivado dos `pagamentos` reais (§5), não mais sintético.

---

## 4. De-para: `pagamentos` → **Payable** (Finance) — *novo*

**Desenho:** agrupar `pagamentos` por `requisitionId` → **1 Payable base por requisição**, com **N parcelas** (uma por `pagamento.seqNum`); cada parcela paga gera **1 baixa** (`finance.payments`). Chave natural do Payable = `origin_requisition_id` (índice único parcial). ⚠️ *Enum storage é misto no One — anotado em cada linha.*

### 4.1 `finance.payables` (head — Σ das parcelas)
| Coluna One | Origem legado | Regra / observação |
|---|---|---|
| `company_id` | unidade(`unitCode`).company | via OrgResolver |
| `origin` **(int)** | `PayableOrigin.Purchase`=1 | externo → `SeniorImport`=3? (Dúvida #1) |
| `vendor_id` | `providerCnpj` → `registry.vendors` | resolvido |
| `counterparty_name` | `providerName` | snapshot do nome |
| `document_number` (null) | — | legado não tem nº NF estruturado → **null** (Decisão #8) |
| `issue_date` | `createdAt` (data) | = competência contábil (Decisão #9) |
| `description` | `requisicoes.description` | trunc 300 |
| `cost_center_id` | `natureName` + company | provisiona natureza se faltar |
| `origin_requisition_id` | id_map(`requisicoes`) | back-ref + **chave de idempotência** |
| `origin_unit_id` | unit.id | |
| `origin_requisition_code` | code (rank estável) | |
| `origin_cycle_competency` (null) | — | Conta Fixa usaria competência (Dúvida #2) |
| `requires_fiscal_document` | tem `fiscalDocumentUrl`? | |
| `requires_tax_analysis` | true p/ Serviço/ContaFixa **a pagar sem retenção** | Decisão #3 |
| `total_amount` | **Σ `originalAmount` das parcelas** | GROSS |
| `status` **(string)** | `PendingAccounting` (default) / `PendingTaxAnalysis` / `Rejected` / `Cancelled` | §5 (Decisões #3, #12, #13) |
| `payment_method` **(int)** | moda de `paymentMethod` | mapa §4.5 |
| `type` **(string)** | `requisicoes.type`: Compra→`Product`, resto→`Service` | |
| `accounting_account_id` (null) | — | Contábil classifica depois |
| `created_by='firestore-migration'`, audit | — | NOT NULL |

### 4.2 `finance.installments` (1 por `pagamento`)
| Coluna | Origem | Regra |
|---|---|---|
| `payable_id` | payable da requisição | |
| `sequence` | `pagamento.seqNum` | 1-based |
| `due_date` | `pagamento.dueDate` | |
| `amount` | `pagamento.originalAmount` | GROSS da parcela |
| `net_amount` | = `amount` | **sem retenção no legado** (Dúvida #3) |
| `paid_amount` | `amount` se pago, senão 0 | |
| `status` **(int)** | mapa de `pagamento.status` | Pago/Contabilizado→`Paid`(2), Fila_Remessa→`InRemittance`(3), Fila_Pagamento/Pendente→`Open`(0) (Dúvida #12) |
| `scheduled_payment_date` | `dueDate` (ou `paymentDate`) | NOT NULL |
| `payment_method` **(int)** | `pagamento.paymentMethod` | mapa §4.5 |
| `boleto_digitable_line` | `linhaDigitavel` | |
| `pix_key_override` | `pixKey` / `pixCopyAndPaste` | |

### 4.3 `finance.payments` (baixa — só para `pagamento` PAGO: Pago/Contabilizado)
| Coluna | Origem | Regra |
|---|---|---|
| `installment_id` / `payable_id` | acima | |
| `payment_date` | `pagamento.paymentDate` | |
| `amount` | `pagamento.amount` | caixa; principal = amount − juros − multa (Dúvida #10) |
| `bank_account_id` **NOT NULL** | `pagamento → remessa → santanderConfigId → conta` | conta importada de `santander_config` (Decisão #4; fallback §8) |
| `method` **(int)** | `paymentMethod` | mapa §4.5 |
| `interest` / `fine` | `juros` / `multa` | 0 na maioria (Decisão #10) |
| `discount` | 0 | |
| `is_reversed` | `status==Devolvido` | senão false |
| `proof_file_name/…` | `paymentReceiptName/Url` | blob migrado (Decisão #6) |
| `created_by_id` **uuid NOT NULL** | usuário **"Migração (sistema)"** (GUID fixo) | Decisão #5 |

### 4.4 `pagamentos` (pagos) → `budgeting.requisition_payment` (REALIZADO — fidelidade ao orçado)
| Coluna | Origem |
|---|---|
| `competency` | `pagamento.competency` |
| `amount` | `pagamento.originalAmount` (principal — fecha contra a estimativa; juros/multa não consomem orçamento) |
| `paid_at` | `pagamento.paymentDate` |
| `payment_id` / `payable_id` | os GUIDs gerados (payment_id **único**) |
| `reversed` | `status==Devolvido` |

→ Realizado por competência = Σ dos pagamentos pagos. É isto que faz o gráfico de consumo bater com o legado.

### 4.5 Mapa `paymentMethod` (string legado → `PaymentMethod` **int**)
`transferência bancária`→`Ted`(1) · `boleto bancário`→`Boleto`(2) · `chave pix`/`pix copia e cola`→`Pix`(0) · `informar posteriormente`→`Other`(5).

### 4.6 `fiscal_documents` (quando há `fiscalDocumentUrl`) e `payable_attachments`
- `fiscal_documents`: `format`=`Pdf`(2, int), `raw_extraction_json`=`'{}'` (NOT NULL), `file_name/storage_path/content_type/size` do blob, `document_number`/`access_key`/impostos = **null** (não temos NF parseada — Dúvida #7).
- `payable_attachments`: boleto (`kind='BankSlip'`), comprovante e NF-PDF (`kind='FiscalDocument'`/`'Other'`), `storage_path` = caminho legado (Dúvida #6).

---

## 5. Status: mapa (decidido — ver Decisões #12 e #13)
Todos os títulos entram **pendentes de contabilidade** (`PendingAccounting`, `accounting_account_id=null`) para o Contábil classificar no One — **mesmo os já pagos** (com baixas + arquivos preservados). Exceção: Serviço/Conta Fixa **a pagar** e **sem retenção** → `PendingTaxAnalysis` (Tributário avalia a retenção primeiro).

| `pagamento.status` | `installments.status` (int) | cria baixa `finance.payments`? | efeito no `Payable.status` |
|---|---|---|---|
| Pago / Contabilizado | Paid (2) | ✅ | `PendingAccounting` (título pago, aguarda classificação) |
| Fila_Remessa | InRemittance (3) | ❌ | `PendingAccounting` / `PendingTaxAnalysis`* |
| Fila_Pagamento / Pendente_Aprovação / Avaliacao_Imposto | Open (0) | ❌ | `PendingAccounting` / `PendingTaxAnalysis`* |
| Devolvido | Open (0) | ❌ | **`Rejected`** (`rejection_reason` do `returnStatus`) |
| Cancelado | (pula a parcela) | ❌ | **`Cancelled`** (se todas canceladas) |

`*` `PendingTaxAnalysis` quando o Payable é Serviço/Conta Fixa, ainda a pagar e sem retenção.

> ⚠️ Verificação técnica pendente: como um título **pago no legado** gravado com `PendingAccounting` + baixas se comporta na tela **Revisão de Contabilidade** (não deve sumir por já ter baixa). Ver §8.

---

## 6. Enums — checklist para o writer Dapper (storage MISTO)
| Enum | Coluna | Storage |
|---|---|---|
| `PayableStatus` | `payables.status` | **string** (Received, Scheduled, Paid, Cancelled, …) |
| `PayableOrigin` | `payables.origin` | **int** (Purchase=1, SeniorImport=3) |
| `PayableType` | `payables.type` | **string** (Service, Product) |
| `PaymentMethod` | `payables/installments/payments` | **int** (Pix=0, Ted=1, Boleto=2, Cash=3, DirectDebit=4, Other=5) |
| `InstallmentStatus` | `installments.status` | **int** (Open=0, PartiallyPaid=1, Paid=2, InRemittance=3) |
| `WithholdingTaxType` | `payable_withholdings.tax_type` | **string** |
| `FiscalDocumentFormat` | `fiscal_documents.format` | **int** (Pdf=2) |
| `PayableAttachmentKind` | `payable_attachments.kind` | **string** |

Chaves naturais (upsert/idempotência): Payable = `origin_requisition_id` (único parcial, `origin_cycle_competency IS NULL`); parcela/baixa/attachment via `id_map` por `pagamento.id`.

---

## 7. ✅ Decisões (28/08) — respostas do responsável

1. **Escopo `isExternal` (85% externos):** **fora desta fase** — importar só `isExternal != true`. Elaborar pergunta ao responsável do legado (o que são os externos, de onde vêm, como um externo aparece no sistema). Ver **§9**.
2. **Recorrência/Conta Fixa:** modo **descontinuado** → **1 Payable-ciclo por mês** (`origin_cycle_competency` = competência do pagamento), 1 parcela + 1 baixa cada.
3. **Retenções + Tributário:** **não** fabricar retenção. Payable **a pagar**, **sem retenção**, tipo **Serviço ou Conta Fixa** → `requires_tax_analysis=true` + status **`PendingTaxAnalysis`** (Tributário avalia a retenção no One).
4. **Conta bancária:** **importar `santander_config`** → `finance.bank_accounts`; resolver `payments.bank_account_id` via `pagamento → remessa (bankRemittanceId / paymentIds[]) → santanderConfigId → conta`. (Fallback p/ pagamento sem remessa: §8.)
5. **Usuário de sistema:** seed de **"Migração (sistema)"** (GUID fixo) p/ `created_by_id` / `uploaded_by_id`.
6. **Blobs:** **migrar os arquivos** do Firebase Storage → **blob do One**; `storage_path` aponta pro caminho do One.
7. **Fiscal document:** com o arquivo migrado, `fiscal_documents` (`format=Pdf`, `raw_extraction_json='{}'`) + `payable_attachment` (`kind=FiscalDocument`); sem parse (chave/nº/impostos null).
8. **Nº da NF:** o **legado não tem** estruturado (confirmado: nem em `pagamentos` nem em `aiTriagemAnalysis`) → `document_number = null`. (O One tem o campo; a origem é que não.)
9. **`issue_date` (competência contábil):** = **`createdAt`** (data) — do `requisicao` p/ Payable base; do `pagamento` p/ Payable-ciclo.
10. **`amount`/`originalAmount`:** `originalAmount` = **principal** (→ `installment.amount`); `amount` = **realmente pago** (→ `payments.amount`), `juros`→`interest`, `multa`→`fine`. **Realizado do orçamento usa o principal (`originalAmount`)** — juros/multa não consomem orçamento.
11. **Pagamentos órfãos** (`requisitionId` sem `requisicao`): **fora desta fase** (junto com os externos), **listados na pergunta (§9)**. Se forem migração legítima, entram depois como Payable `Origin=SeniorImport` sem `origin_requisition_id`.
12. **Status Devolvido/Cancelado:** `Devolvido` → Payable **`Rejected`**; `Cancelado` → Payable **`Cancelled`**. Mapa completo em **§5**.
13. **Contábil:** **sem journal**. Todos os títulos entram **pendentes de contabilidade** (`PendingAccounting`) p/ o Contábil classificar no One — **com os arquivos anexados até nos já pagos** (histórico/realizado preservados).
14. **Front-load / pré-aprovação com pagamento:** **importar sim** (marcar para validação).

## 8. Status de implementação

**✅ Implementado (commits 84dc3ae9 + 9b564ce0):**
- `santander_config` → `finance.bank_accounts` (BankAccountWriter); `remessas` indexadas p/ resolver a conta de cada baixa; fallback conta `default`.
- `pagamentos` (fluxo) → `finance.payables` + `installments` + `payments`; realizado real em `budgeting.requisition_payment` (principal por competência); RequisitionWriter não grava mais pagamento sintético.
- Migração de blob: BlobMigrator (Firebase Storage `spherical-cell-g07pf.firebasestorage.app` → blob do One via `IFileStorageService` convention) + PayableFileWriter (`fiscal_documents` + `payable_attachments`), flag `payableFiles`.
- Ator de sistema `SystemActor.Id` (…05ed); enums Finance com storage misto respeitados.

**⚠️ Desvio conhecido (a confirmar):** recorrência/Conta Fixa (Decisão #2 pedia **1 Payable-ciclo por mês**). Nesta 1ª versão TODAS as requisições viram **1 Payable base com N parcelas** (inclusive as recorrentes). Como recorrência é modo descontinuado (155 casos) e as datas/valores ficam fiéis, priorizei o caminho único; o split em Payable-ciclo (`origin_cycle_competency` por mês) fica como refinamento se você quiser.

**⏳ A validar ao vivo (precisa dev + Azurite):**
- Título **pago no legado** gravado como `PendingAccounting` + baixas aparece certo na **Revisão de Contabilidade** (não some por ter baixa) e o **`PendingTaxAnalysis`** cai na fila do Tributário.
- Chain completo `payables` + `payable-files` contra o Postgres/Azurite locais (contagens, `deadLettered`, arquivos servíveis pela app).
- Fallback de `bank_account_id` para baixas sem remessa (a maioria é `returnStatus=Manual`).

## 9. ✉️ Pergunta ao responsável do legado (externos + órfãos)
Ver rascunho pronto para envio: **`docs/pergunta-legado-externos.md`** (gerado à parte).

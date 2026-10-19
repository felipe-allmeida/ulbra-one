# Importador Orçamento (Firestore) → Ulbra One (Postgres)

Job standalone (desacoplado do monólito) que migra os dados do sistema legado **orcamento**
(Firestore) para o Postgres do Ulbra One via **Dapper**, em batches e de forma **idempotente**.

**Fase 1 — 3 collections + consumo:** `fornecedores` → `registry.vendors`, `usuarios` → `identity.users`
(+ scope grants), `orcamentos` → `core.cost_center_unit_budgets` (+ cobertura `core.cost_center_units`), e
as **solicitações** (`requisicoes` → `budgeting.requisitions` + `requisition_installment` + `requisition_payment`),
que alimentam o **consumo** (realizado + empenhado) de cada orçamento sem depender de Payables.

## Projetos

- `Ulbra.One.FirestoreImport` — biblioteca: leitura Firestore, resolver de organograma, writers Dapper,
  idempotência. Sem referência ao monólito (invariantes portadas em `Mapping/`).
- `Ulbra.One.FirestoreImport.Functions` — host Azure Functions (HTTP trigger, isolated worker net10).
- `Ulbra.One.FirestoreImport.Tests` — testes unitários dos mapeadores.

## Endpoints (Postman)

Três rotas: valida, **dispara (assíncrono)** e acompanha.

| Rota | O que faz |
|---|---|
| `POST /api/import/preflight` | Mede a cobertura de org **no ambiente-alvo**: bloqueios (unidade ausente) e naturezas a provisionar. **Não grava** (síncrono). |
| `POST /api/import` | **Dispara** a migração em **background** e retorna **`202`** na hora + `{ runId, statusUrl }`. O corpo (JSON de bools) diz **o que** migrar; roda os itens marcados **sequencialmente e na ordem de dependência**. Single-writer: `409` se já houver um run em andamento. |
| `GET /api/import/status/{runId}` | Acompanha o run: `Running`/`Completed`/`Failed` + o resumo por item quando termina. |

**Por que assíncrono:** um run completo lê dezenas de milhares de docs do Firestore e leva minutos — além de qualquer timeout de cliente (dev) ou do ingress do Container App (prod). Como o `POST` volta na hora, funciona igual em dev e prod. Sem checkpoint entre restarts (estado in-memory), mas o import é idempotente (`id_map`/`content_hash`), então re-rodar continua de onde parou.

Corpo do `import` (todo campo é opcional; ausente = `false`):

```json
{
  "dryRun": false,
  "vendors": true,
  "users": true,
  "budgets": true,
  "bankAccounts": true,
  "requisitionsPaid": true,
  "requisitionsInPayment": true,
  "requisitionsInApproval": false,
  "payables": true,
  "payableFiles": true
}
```

Ordem forçada internamente: **vendors → budgets → users → bankAccounts → requisitions (paid → in payment → in approval) → payables → payableFiles**.

**Fase 2 (financeiro):** `bankAccounts` (`santander_config` → `finance.bank_accounts`), `payables` (Firestore `pagamentos` → `finance.payables` + parcelas + baixas, e o **realizado real** do orçamento em `budgeting.requisition_payment`), e `payableFiles` (migra NF/boleto/comprovante do Firebase Storage → blob do One + `fiscal_documents`/`payable_attachments`). Rode `payables` **depois** das requisições e de `bankAccounts`; `payableFiles` **depois** de `payables` (é lento — baixa+sobe cada PDF). Baldes externos `EXT_REQ_IMP_*` ficam fora (ver `docs/pergunta-legado-externos.md`).

As flags de requisição são **por estágio de consumo** do orçamento (o status decide o que ela vira):

| Flag | Statuses legado | Consumo |
|---|---|---|
| `requisitionsPaid` | PAGO, Finalizada | **realizado** (grava parcelas + `requisition_payment` por parcela) |
| `requisitionsInPayment` | Em Pagamento, Aguardando Doc. Fiscal | **empenhado** |
| `requisitionsInApproval` | Triagem, Aprovação, Complementação | **previsto** |

(Cancelado não entra em estágio nenhum — nunca é importado.)

Resposta: `{ dryRun, vendors, budgets, users, requisitionsPaid, … }` — um resumo por item pedido (`null` quando a flag ficou off), cada um `{ entity, dryRun, read, inserted, updated, skipped, deadLettered }`.

**Migração sob-demanda (uso contínuo):** re-rode quando quiser. O `id_map`+`content_hash` identifica o que é **novo** (`inserted`), o que **mudou** (`updated`) e o que está **igual** (`skipped`). Uma solicitação que avançou de estágio (ex.: Em Pagamento → PAGO) é **promovida in-place** quando a flag do novo estágio roda — o `code` sintético é rank **global** por id legado, estável entre execuções e entre flags. Rode com `"dryRun": true` primeiro.

**Parcelas:** cada solicitação reproduz seu plano real (`requisition_installment`) a partir de `paymentInstallmentsCount` + `paymentFrequency` (Mensal=+1 mês, Quinzenal=2 parcelas/mês) + `paymentFirstDueDate`; a soma das parcelas == `estimate_value` (invariante do domínio). Para as pagas, grava 1 `requisition_payment` por parcela na competência do vencimento.

**Sequencial, não paralelo:** um writer único, uma linha por vez, um item após o outro (nunca concorrente).

**Mesma config em dev/prod:** o código e as chaves de config são iguais em todo ambiente — só mudam os **valores** (`ConnectionStrings:Database` e a credencial do Firestore), injetados por ambiente (local → seu Postgres; deploy → Key Vault). Rodando local, o import grava **no seu banco local**.

## Idempotência & dead-letter

Schema próprio `orcamento_import` (criado no 1º run):
- `id_map` — cada doc legado → GUID alvo fixo + `content_hash` (re-run: `skipped` se nada mudou).
- `dead_letter` — refs não resolvidas (unidade/natureza ausente, doc inválido) sem abortar o lote.

## Configuração

`ConnectionStrings:Database` (mesmo formato do API) e a seção `Firestore`:

```
Firestore:ProjectId    = spherical-cell-g07pf
Firestore:DatabaseId   = ai-studio-9c0ba35e-15c2-4fa9-96ad-eb738fcf5bb5   (banco NOMEADO)
Firestore:CredentialsPath = <opcional; caminho do SA key JSON>
```

- **Local:** credenciais do Firestore via ADC (`gcloud auth application-default login`). `local.settings.json`
  já aponta o Postgres de dev (localhost:5433) e o projeto/banco do Firestore.
- **Rodar local:** `func start` (Azure Functions Core Tools) na pasta do host e chamar os endpoints, ou o
  CLI: `dotnet run --project tools/Ulbra.One.FirestoreImport.Cli -- <itens> [--apply]` — itens
  `vendors users budgets bank-accounts requisitions-paid requisitions-in-payment requisitions-in-approval
  payables payable-files`, ou os atalhos `all` (os 3 da Fase 1) e `requisitions` (os 3 estágios). Sem `--apply`
  é dry-run; sem item, roda o preflight.

## Pré-condições

- As migrations do API já aplicadas no Postgres-alvo (o job **não** cria o schema de negócio).
- Organograma do alvo completo. Rode `preflight` para medir; o gap conhecido do seed é preenchido por
  `DevDataSeeder.EnsureImportOrgCompleteAsync` (4 naturezas + unidade 604), idempotente.

## Deploy (pendente — greenfield neste repo)

O repo não tem infra de Functions (o padrão é ACA Job). Para publicar como Azure Function faltam, no
Terraform: Function App + workflow de deploy, managed identity + KV secret reference para a connection
string (role no `kv-ulbraone-prod`), e **credencial do GCP a partir do Azure** (SA key em KV ou Workload
Identity Federation). Ver o plano em `~/.claude/plans/`.

## Testes

```
dotnet test tools/Ulbra.One.FirestoreImport.Tests
```

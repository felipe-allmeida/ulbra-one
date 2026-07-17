# Runbook — Homologação CNAB240 de pagamento com o Santander

Checklist operacional para homologar o gerador de remessa CNAB240 (pagamento a fornecedores) com o
Banco Santander (033) usando um arquivo **real**, e para validar o fluxo de geração ponta a ponta.

Relaciona: AEL-765 (homologação), AEL-613 (gerador), AEL-618 (retorno `.RET`), AEL-607 (dados
bancários do fornecedor). O motor passa em golden-file contra o manual Santander **YLEC_2403
v11.3.1**; falta o aceite de layout com o banco.

---

## 1. Pré-requisitos (dados)

- [ ] **Conta pagadora Santander** cadastrada (`/app/bank-accounts`): `bankCode` Santander (aceita
  `33`/`0033`/`033` — é canonicalizado para `033`), agência, conta e **dígito** dentro das larguras
  CNAB (agência ≤ 5 dígitos, conta ≤ 12, dígito 1 caractere).
- [ ] **Código de convênio** (`cnabAgreementCode`, 12 dígitos) preenchido na conta pagadora — sem ele
  a geração falha com `MissingAgreementCode` (422).
- [ ] Conta marcada como **padrão de pagamento** (`isDefaultForPayments`) se o job diário (AEL-766)
  também for exercitar essa conta.
- [ ] **Fornecedores** com dados bancários estruturados (`BankInfo`): TED exige banco+agência+conta;
  PIX exige a chave. Larguras: banco 3 dígitos, agência ≤ 5, conta ≤ 12, dígito 1, documento CPF/CNPJ
  (11/14 dígitos). Dados fora da largura são **pulados com motivo** (não quebram a geração).
- [ ] **CNPJ da empresa** (Core) válido (≤ 14 dígitos).

> A validação de largura/formato (payer + favorecido) roda antes de montar o arquivo: dado inválido
> vira skip por parcela (favorecido) ou erro `InvalidPayerData` (422, payer) — nunca HTTP 500.

## 2. Habilitar o CNAB pagamento no banco

- [ ] Solicitar/confirmar com o Santander a habilitação do produto "Pagamento a Fornecedores"
  (CNAB240) para a conta PJ e obter a **janela de homologação de layout**.
- [ ] Confirmar o **código de convênio** oficial e cadastrá-lo na conta pagadora.

## 3. Gerar a remessa real (`.REM`)

- [ ] Em `/app/remittances`, "Gerar remessa" → escolher a conta pagadora Santander (o diálogo só lista
  contas elegíveis) + vencimento de corte.
- [ ] Conferir o toast: títulos incluídos e parcelas ignoradas (com motivo).
- [ ] Baixar o `.REM` e validar o arquivo:
  - [ ] Todos os registros com **240 posições**, terminador **CRLF**, codificação **Latin-1**.
  - [ ] **Segmento A** (TED): câmara `018`, banco/agência/conta do favorecido, finalidade;
    **Segmento B** com CPF/CNPJ (e chave PIX na Informação 12 para PIX).
  - [ ] **Convênio** (20 bytes) = `0033` + agência(4) + convênio(12).
  - [ ] "Seu Número" (Seg. A pos 74-93) = `Reference` (`{seqRemessa:D6}{seqLinha:D5}`) — nunca truncado.
- [ ] Submeter o `.REM` no portal do banco (homologação).

## 4. Divergências conhecidas a confirmar 🔶

- [ ] **Forma de lançamento TED** (`SantanderCnabProfile.FormaTed`): emitimos `03` (Santander G002);
  o padrão FEBRABAN é `41`. **Confirmar** o valor aceito na homologação e ajustar o adapter se preciso.
- [ ] **PIX Segmento B**: forma de iniciação (email/telefone/CPF-CNPJ/EVP) e sub-limites da Informação
  12 — validar contra o retorno do banco.
- [ ] Registrar qualquer outra divergência de layout apontada pelo banco e ajustar
  `SantanderCnabProfile` (`ICnabBankProfile`) com teste golden atualizado.

## 5. Retorno `.RET` real + conciliação

- [ ] Capturar um `.RET` **de produção** (não simulado) e importar em `/app/remittances` → detalhe →
  "Importar retorno".
- [ ] Confirmar o casamento por `Reference` e as transições: `Pago` (cria `Payment`, dispara a
  liquidação contábil) / `Devolvido` (volta para `Scheduled`) / divergente (fica pendente de revisão).
- [ ] **Validar com a Contabilidade** a tabela de **códigos de ocorrência** Santander
  (`SantanderPaidCodes` / `SantanderRejectedCodes`) e a regra de divergência — hoje best-effort a
  partir de FEBRABAN v10.11 + YLEC_2403 v11.3.1.

## 6. Validação de fluxo em dev (sem banco)

- [ ] Subir o ambiente (`dotnet run --project src/Ulbra.One.AppHost`) com dados de seed.
- [ ] Gerar remessa para uma conta Santander válida → baixar `.REM` → conferir contra o golden.
- [ ] Exercitar os ramos de erro/skip e confirmar mensagens limpas (sem 500):
  - conta não-Santander → `BankNotSupported` (422);
  - convênio ausente → `MissingAgreementCode` (422);
  - fornecedor sem/inválido dado bancário → parcela **pulada com motivo**;
  - nada elegível → `NothingToRemit` (400);
  - conta/empresa inexistente → 404.
- [ ] (dev) "Simular retorno" para exercitar o loop de conciliação sem banco.

## 7. Encerramento

- [ ] Anexar o `.REM`/`.RET` de homologação e o parecer de aceite do banco à issue AEL-765.
- [ ] Registrar divergências corrigidas + versão do layout homologada.
- [ ] Confirmar que o job diário (AEL-766) usa a mesma conta/convênio homologados.

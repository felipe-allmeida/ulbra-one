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

### 4.1 Boleto / Convênio (Seg J / J-52 / O) 🔶 — em implementação

Cobertura sendo adicionada (AEL-613). Confirmar contra o arquivo real do Santander:

- [ ] **Boleto bancário** → Segmento **J** + **J-52**. `tipo_servico`/`forma`/`versao` de lote
  (candidatos do reference: `20`/`31`/`030`) — reconfirmar YLEC v11.3.1.
- [ ] **Convênio/concessionária/tributo** (código de barras iniciando com `8`) → Segmento **O**.
  Lote `tipo_servico`/`forma`/`versao` candidatos: `22`/`11`/`010`.
- [ ] **Segmento J-52**: o gerador de referência grava layout que **diverge do FEBRABAN** (espaço na
  pos 15, `0052` em 16-19) — **não** replicar cegamente; derivar do YLEC e validar.
- [ ] **Código de barras**: reordenação linha digitável→44 posições (bancário 47→44 e convênio 48→44)
  e conferência de DVs (mod-10/11) via `BoletoBarcode`.
- [ ] **Lote monocategoria**: boleto bancário e convênio nunca no mesmo lote (lotes/remessas separados).
- [ ] **Segmento B não-PIX** (TED): endereço do favorecido + vencimento + valores (hoje em branco).
- [ ] **`dv_agencia`**: pagador (header pos 58) via `BankAccount.AgencyDigit`; favorecido (Seg A pos 29)
  via `BankInfo.BranchDigit`.
- [ ] **Retorno** (AEL-618): parser lê Seg J/O, captura Segmento **Z** (autenticação), checa
  divergência de somatório do trailer, e casa por CNPJ+valor além do "Seu Número".

### 4.2 Golden file real do Santander (pendência transversal) ⭐

- [ ] **Obter do Financeiro** um `.REM` real aceito + um `.RET` real (anonimizar CNPJ/contas/valores se
  o ambiente não for confiável). Até então, cada fase valida contra o **golden sintético** (§7 do doc
  de referência) + testes inline.
- [ ] Fazer **diff byte-a-byte** contra a saída do writer por método e resolver os 🔶 do
  `docs/cnab240-layout.md` (versão `060` vs `206`, forma TED `03` vs `41`, códigos de
  boleto/convênio, sub-limites Seg B/J-52/O). Remover os 🔶 confirmados.

## 5. Retorno `.RET` real + conciliação

> **✅ Parser já validado byte-a-byte contra um `.RET` real** (FORN, 15/07/2026, 120 pagamentos PIX/TED).
> `Cnab240ReturnParser` leu as 120 entradas com as posições corretas (Seu Número 74, Nosso Número 135,
> data 155, valor 163, ocorrência 231, Seg B doc 19-32, Seg Z auth 15-78); a soma dos detalhes conciliou
> com a somatória do trailer ao centavo (**R$ 931.926,95**), 117 com autenticação Seg Z. Ver
> `docs/cnab240-layout.md`. **Ainda pendente**: (a) um `.RET` com **boleto (Seg J)** e **convênio (Seg O)**
> para validar as posições de retorno desses segmentos; (b) um `.RET` com **rejeições** para exercitar
> `SantanderRejectedCodes`; (c) classificar os códigos `PA`/`PF` (observados em registros de movimento
> `333` sem Seg Z → hoje `Unknown`) contra a tabela de ocorrências do manual.

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

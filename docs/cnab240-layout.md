# CNAB 240 — Santander (033) — mapa de campos autoritativo

> **Fonte da verdade do layout de campos** para o gerador/parser de remessa CNAB 240 do Banco
> Santander (033) no ulbra.one. Base: manual Santander **YLEC_2403 v11.3.1** sobre FEBRABAN v10.11.
> Referenciado por `SantanderCnabProfile.cs`, `CnabFieldValidation.cs`, `Cnab240ReturnParser.cs` e
> pelos testes golden.
>
> **Posições são 1-indexed** dentro da linha de 240. `NUM` = numérico (zeros à esquerda, via
> `CnabRecordBuilder.Numeric/Money/Date`); `ALFA` = alfanumérico (espaços à direita, maiúsculo, sem
> acento, via `Alpha`/`AlphaStrict`). Todo registro cobre exatamente 240 posições — garantido por
> `CnabRecordBuilder.BuildComplete()`. Terminador `\r\n`; encoding `Latin1` (ISO-8859-1).
>
> **Legenda de status:** ✅ implementado hoje · 🔶 a implementar / confirmar contra o manual YLEC e
> o arquivo real do Santander (homologação) antes de tratar como definitivo.

---

## Registros por arquivo

| Tipo | Registro | Qtd | Status |
|---|---|---|---|
| 0 | Header de Arquivo | 1 | ✅ |
| 1 | Header de Lote | 1 por lote (1 lote por categoria) | ✅ |
| 3 | Detalhe — Seg A + B (PIX/TED) | N | ✅ |
| 3 | Detalhe — Seg J + J-52 (boleto bancário) | N | 🔶 |
| 3 | Detalhe — Seg O (convênio/tributo) | N | 🔶 |
| 5 | Trailer de Lote | 1 por lote | ✅ |
| 9 | Trailer de Arquivo | 1 | ✅ |

**Categorias de lote** (`CnabLoteCategory`) e segmentos emitidos por linha:
`Pix`/`Ted` → `[A, B]` ✅ · `BoletoBancario` → `[J, J52]` 🔶 · `Convenio` → `[O]` 🔶.
Roteamento: `PaymentMethod.Boleto` cujo código de barras inicia com `8` → `Convenio`, senão
`BoletoBancario`. Boleto bancário e convênio caem em **lotes distintos** (lote monocategoria).

---

## Record 0 — Header de Arquivo ✅

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 1-3 | 3 | NUM | codigo_banco | `033` |
| 4-7 | 4 | NUM | lote_servico | `0000` |
| 8 | 1 | NUM | tipo_registro | `0` |
| 9-17 | 9 | — | filler FEBRABAN | brancos |
| 18 | 1 | NUM | tipo_inscricao | `2` (CNPJ) |
| 19-32 | 14 | NUM | numero_inscricao | CNPJ pagador |
| 33-52 | 20 | ALFA | codigo_convenio | `AgreementField` (convênio) |
| 53-57 | 5 | NUM | agencia_mantenedora | agência pagador |
| 58 | 1 | — | dv_agencia | ✅ `BankAccount.AgencyDigit` (branco quando não informado) |
| 59-70 | 12 | NUM | numero_conta_corrente | conta pagador |
| 71 | 1 | ALFA | dv_conta | dígito conta |
| 72 | 1 | — | dv_agencia_conta | branco (Santander) |
| 73-102 | 30 | ALFA | nome_empresa | razão social (trunca) |
| 103-132 | 30 | ALFA | nome_banco | `BANCO SANTANDER` (gerador de referência / remessa aceita) |
| 133-142 | 10 | — | filler FEBRABAN | brancos |
| 143 | 1 | NUM | codigo_remessa | `1` (remessa) |
| 144-151 | 8 | NUM | data_geracao | DDMMAAAA (servidor) |
| 152-157 | 6 | NUM | hora_geracao | HHMMSS (servidor) |
| 158-163 | 6 | NUM | NSA | sequencial persistido por conta (max+1) |
| 164-166 | 3 | NUM | versao_layout_arquivo | `206` ✅ emitido (manual YLEC v11.7 pág. 8 diz `060`; decisão AEL-910: espelhar as remessas aceitas em produção, que usam `206` — o legado também gera `206`) |
| 167-171 | 5 | — | densidade_gravacao | branco (AEL-910: remessa aceita deixa em branco; legado idem) |
| 172-240 | — | — | reservados / ocorrências | brancos (retorno preenche 231-240) |

## Record 1 — Header de Lote ✅

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 1-3 | 3 | NUM | codigo_banco | `033` |
| 4-7 | 4 | NUM | lote_servico | nº do lote |
| 8 | 1 | NUM | tipo_registro | `1` |
| 9 | 1 | ALFA | tipo_operacao | `C` (crédito) |
| 10-11 | 2 | NUM | tipo_servico | `20` fornecedores (PIX/TED/boleto) · `22` tributos/convênio 🔶 |
| 12-13 | 2 | NUM | forma_lancamento | PIX `45` · TED `03` 🔶(FEBRABAN `41`) · boleto `31` 🔶 · convênio `11` 🔶 |
| 14-16 | 3 | NUM | versao_layout_lote | PIX/TED `031` · boleto `030` 🔶 · convênio `010` 🔶 |
| 17 | 1 | — | filler | branco |
| 18 | 1 | NUM | tipo_inscricao_empresa | `2` (CNPJ) |
| 19-32 | 14 | NUM | numero_inscricao_empresa | CNPJ pagador |
| 33-52 | 20 | ALFA | codigo_convenio | convênio |
| 53-57 | 5 | NUM | agencia_mantenedora | agência pagador |
| 58 | 1 | — | dv_agencia | ✅ `BankAccount.AgencyDigit` (branco quando não informado) |
| 59-70 | 12 | NUM | numero_conta_corrente | conta pagador |
| 71 | 1 | ALFA | dv_conta | dígito conta |
| 72 | 1 | — | dv_agencia_conta | branco |
| 73-102 | 30 | ALFA | nome_empresa | razão social |
| 103-142 | 40 | — | informacao_1 (mensagem) | brancos |
| 143-172 | 30 | ALFA | endereco_logradouro | ✅ `BankAccount.AddressStreet` (branco se não configurado) |
| 173-177 | 5 | NUM | endereco_numero | ✅ `BankAccount.AddressNumber` |
| 178-192 | 15 | ALFA | endereco_complemento | ✅ `BankAccount.AddressComplement` |
| 193-212 | 20 | ALFA | endereco_cidade | ✅ `BankAccount.AddressCity` |
| 213-217 | 5 | NUM | endereco_cep | ✅ `BankAccount.AddressCep` (5 primeiros dígitos) |
| 218-220 | 3 | NUM | endereco_cep_complemento | ✅ `BankAccount.AddressCep` (3 dígitos finais) |
| 221-222 | 2 | ALFA | endereco_uf | ✅ `BankAccount.AddressState` |
| 223-240 | — | — | filler / ocorrências | brancos |

## Record 3 — Segmento A (PIX / TED) ✅

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 1-3 / 4-7 / 8 | — | NUM | banco / lote / tipo | `033` / lote / `3` |
| 9-13 | 5 | NUM | numero_sequencial_registro_lote | contador do writer |
| 14 | 1 | ALFA | codigo_segmento | `A` |
| 15 | 1 | NUM | tipo_movimento | `0` (inclusão) |
| 16-17 | 2 | NUM | codigo_instrucao_movimento | `00` |
| 18-20 | 3 | NUM | codigo_camara | TED `018` (CIP) · PIX `009` (Nota G014 — conformidade C1) |
| 21-23 | 3 | NUM | codigo_banco_favorecido | TED: banco favorecido · PIX: `000` |
| 24-28 | 5 | NUM | agencia_favorecido | TED: agência · PIX: `0` |
| 29 | 1 | — | dv_agencia (favorecido) | branco (conformidade C3 — este layout só carrega o DV da conta em pos 42, não há DV de agência do favorecido) |
| 30-41 | 12 | NUM | conta_favorecido | TED: conta · PIX: `0` |
| 42 | 1 | ALFA | dv_conta (favorecido) | TED: dígito · PIX: branco |
| 43 | 1 | — | dv_agencia_conta | branco |
| 44-73 | 30 | ALFA | nome_favorecido | nome (trunca) |
| 74-93 | 20 | ALFA | "Seu Número" | `Reference` = NSA[6]+seq[5] — **nunca trunca** (`AlphaStrict`) |
| 94-101 | 8 | NUM | data_pagamento | DDMMAAAA |
| 102-104 | 3 | ALFA | tipo_moeda | `BRL` |
| 105-119 | 15 | NUM | quantidade_moeda | `0` |
| 120-134 | 15 | NUM | valor_pagamento | centavos |
| 135-154 | 20 | — | "Nosso Número" | branco (banco preenche no retorno) |
| 155-162 | 8 | NUM | data_real_efetivacao | `0` na remessa |
| 163-177 | 15 | NUM | valor_real_efetivacao | `0` na remessa |
| 178-217 | 40 | — | outras_informacoes | brancos |
| 218-219 | 2 | — | complemento_tipo_servico | brancos |
| 220-224 | 5 | ALFA | codigo_finalidade_ted | TED `00001` · PIX branco |
| 225-229 | — | — | complemento / exclusivo FEBRABAN | brancos |
| 230 | 1 | NUM | aviso_favorecido | `0` |
| 231-240 | 10 | — | ocorrencias_retorno | brancos |

## Record 3 — Segmento B ✅ (ramo PIX) / 🔶 (ramo não-PIX a enriquecer)

Cabeçalho comum:

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 1-13 | — | — | banco/lote/tipo/seq | como Seg A, `codigo_segmento = B` |
| 15-16 | 2 | NUM | forma_iniciacao | PIX: tipo de chave (`01`..`04`) · não-PIX: `0` |
| 17 | 1 | — | uso_reservado_banco | branco |
| 18 | 1 | NUM | tipo_inscricao_favorecido | 11 díg→`1` · 14→`2` · senão `0` 🔶(guard) |
| 19-32 | 14 | NUM | numero_inscricao_favorecido | CNPJ/CPF favorecido |

**Ramo PIX** ✅:

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 33-67 | 35 | — | Informação 10 (TXID) | branco |
| 68-127 | 60 | — | Informação 11 | branco |
| 128-226 | 99 | ALFA | Informação 12 — CHAVE PIX | chave (trunca) — 🔶 sub-limite por tipo a confirmar |
| 227-240 | — | — | reservado / ISPB | brancos |

**Ramo não-PIX (TED/boleto-fallback)** ✅ — endereço do favorecido + vencimento + valores,
consumidos do snapshot em `RemittanceLine` (`FavoredAddress*`, `DueDate`, `Amount`).
🔶 sub-limites FEBRABAN v10.11; confirmar YLEC + arquivo real na homologação.

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 33-62 | 30 | ALFA | endereco_logradouro | `FavoredAddressStreet` |
| 63-67 | 5 | NUM | endereco_numero | `FavoredAddressNumber` |
| 68-82 | 15 | ALFA | endereco_complemento | `FavoredAddressComplement` |
| 83-97 | 15 | ALFA | endereco_bairro | `FavoredAddressNeighborhood` |
| 98-117 | 20 | ALFA | endereco_cidade | `FavoredAddressCity` |
| 118-125 | 8 | NUM | endereco_cep | `FavoredAddressCep` |
| 126-127 | 2 | ALFA | endereco_uf | `FavoredAddressState` |
| 128-135 | 8 | NUM | data_vencimento | `DueDate` (fallback `PaymentDate`) |
| 136-150 | 15 | NUM | valor_nominal | `Amount` (centavos) |
| 151-165 | 15 | NUM | abatimento | `0` |
| 166-180 | 15 | NUM | descontos | `0` |
| 181-195 | 15 | NUM | juros_mora | `0` |
| 196-210 | 15 | NUM | multa | `0` |
| 211-240 | 30 | — | id / valor descritivo / reservado | brancos |

## Record 3 — Segmento J (boleto bancário) 🔶

Emitido quando `PaymentMethod.Boleto` e o código de barras **não** inicia com `8`. A linha digitável
de 47 dígitos é convertida para o código de barras de 44 posições (`BoletoBarcode.BancarioTo44`).

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 1-13 | — | — | banco/lote/tipo/seq | `codigo_segmento = J` |
| 15 | 1 | NUM | tipo_movimento | `0` |
| 16-17 | 2 | NUM | codigo_instrucao | `00` |
| 18-61 | 44 | NUM | codigo_barras | de `BoletoBarcode.BancarioTo44(linhaDigitavel)` |
| 62-91 | 30 | ALFA | nome_cedente/favorecido | nome |
| 92-99 | 8 | NUM | data_vencimento | DDMMAAAA |
| 100-114 | 15 | NUM | valor_nominal | centavos |
| 115-129 | 15 | NUM | desconto+abatimento | `0` |
| 130-144 | 15 | NUM | mora+multa | `0` |
| 145-152 | 8 | NUM | data_pagamento | DDMMAAAA |
| 153-167 | 15 | NUM | valor_pagamento | centavos |
| 168-182 | 15 | NUM | uso FEBRABAN | `0` |
| 183-202 | 20 | ALFA | "Seu Número" | `Reference` |
| 203-222 | 20 | — | "Nosso Número" (banco) | branco |
| 223-224 | 2 | NUM | codigo_moeda | `09` |
| 225-240 | 16 | — | uso FEBRABAN | brancos |

## Record 3 — Segmento J-52 (sacado/cedente) 🔶

Segue o Seg J. Sacado = empresa pagadora; cedente = favorecido. ⚠ O reference grava um layout que
**diverge do FEBRABAN** (espaço na pos 15, `0052` em 16-19) — **não** replicar cegamente; derivar do
YLEC v11.3.1 e validar com arquivo real. Conteúdo lógico: tipo/nº inscrição + nome do sacado
(pagador), tipo/nº inscrição + nome do cedente (favorecido), sacador avalista isento.

## Record 3 — Segmento O (convênio/concessionária/tributo) 🔶

Emitido quando `PaymentMethod.Boleto` e o código de barras inicia com `8` (`BoletoBarcode.ConvenioTo44`).

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 1-13 | — | — | banco/lote/tipo/seq | `codigo_segmento = O` |
| 15 | 1 | NUM | tipo_movimento | `0` |
| 16-17 | 2 | NUM | codigo_instrucao | `00` |
| 18-61 | 44 | NUM | codigo_barras | 44 díg iniciando com `8` |
| 62-91 | 30 | ALFA | nome_cedente | nome |
| 92-99 | 8 | NUM | data_vencimento | DDMMAAAA |
| 100-107 | 8 | NUM | data_pagamento | DDMMAAAA |
| 108-122 | 15 | NUM | valor | centavos |
| 123-142 | 20 | ALFA | "Seu Número" / controle | `Reference` |
| 143-240 | 98 | — | brancos | brancos |

## Record 5 — Trailer de Lote ✅

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 1-8 | — | — | banco/lote/tipo | `033` / lote / `5` |
| 9-17 | 9 | — | filler | brancos |
| 18-23 | 6 | NUM | qtd_registros_lote | header + trailer + detalhes reais (⚠ varia por categoria: A+B=2/linha, J+J52=2/linha, O=1/linha) |
| 24-41 | 18 | NUM | somatoria_valores | Σ centavos |
| 42-59 | 18 | NUM | somatoria_qtd_moedas | `0` |
| 60-65 | 6 | NUM | numero_aviso_debito | `0` |
| 66-240 | — | — | filler / ocorrências | brancos |

## Record 9 — Trailer de Arquivo ✅

| Pos | Tam | Fmt | Campo | Conteúdo |
|---|---|---|---|---|
| 1-3 | 3 | NUM | codigo_banco | `033` |
| 4-7 | 4 | NUM | lote_servico | `9999` |
| 8 | 1 | NUM | tipo_registro | `9` |
| 9-17 | 9 | — | filler | brancos |
| 18-23 | 6 | NUM | qtd_lotes | nº de lotes |
| 24-29 | 6 | NUM | qtd_registros_arquivo | total de registros |
| 30-240 | — | — | filler | brancos |

---

## Validação contra o manual oficial Santander — Pagamento a Fornecedores CNAB 240 v11.7 (jul/2026)

Fonte da verdade da **remessa**. Layout validado campo a campo contra o manual (H— Pagamento a
Fornecedores, não confundir com o manual de **Cobrança** H7815, que é outro produto).

**✅ Confirmado pelo manual (ulbra-one já estava correto):**
- `versao_layout_arquivo`: manual pede **`060`**, mas **emitimos `206`** por decisão (AEL-910) — as remessas aceitas em produção e o sistema legado (orcamento) usam `206`; o banco não valida esse campo estritamente. (Header de Arquivo tabela acima reflete `206`.)
- Forma de lançamento (G002): PIX `45`, TED `03`, boleto `31` (títulos outros bancos), convênio `11`.
- Tipo de serviço (G015): `20` fornecedor, `22` tributos. Versão do lote (G031): PIX/TED `031`, boleto `030`, convênio `010`.
- Modelo multi-lote: "arquivo = 1+ lotes, cada lote com um único tipo de serviço" — ulbra-one OK.
- Seg A: `Data do Pagamento` (94-101), câmara TED `018`. Seg B PIX: forma de iniciação em **15-16** (G032, códigos `01`-`04`), chave em **Informação 12 128-226** (G035). Seg J e Seg O batem byte-a-byte. Header/lote/trailers OK.

**🔧 Corrigido a partir do manual (commit de conformidade):**
- **Câmara PIX (Seg A 18-20)** = **`009`** (Nota G014: 009=Pix); era `000`.
- **Finalidade (Seg A 220-224)** = **`00005`** = Pagamento a Fornecedores (Nota G013 A); era `00001` (Impostos).
- **DV agência do favorecido (Seg A 29)** = **Branco** (o DV do favorecido não é usado; DV vai só na conta, pos 42); ulbra-one gravava o `BranchDigit` indevidamente.
- **Seg J-52** reescrito para o layout do manual §3.4: `16-17` código de movimento remessa (G026=00), `18-19` `52`, pagador `20-75`, beneficiário `76-131`, sacador `132-187`.

**🔶 A validar com um boleto/convênio real:**
- Reordenação exata do código de barras (G008/N002 — o exemplo do manual está em imagem; o princípio "descartar os DVs de campo" bate com `BoletoBarcode`).
- Cauda opcional do Seg B não-PIX (211-240: Horário TED, Código Histórico Crédito G019, ISPB) — hoje em branco (campos opcionais).

**Nota sobre o .RET real (15/07/2026):** aquele arquivo é **RETORNO** — valida o **parser**, mas **não**
define o layout da remessa (o banco reescreve/consolida). A remessa se valida contra o **manual** e/ou
uma **remessa real**, nunca contra retorno.

**✅ Parser de retorno validado byte-a-byte contra esse .RET real** (`Cnab240ReturnParser`, 120 pagamentos,
todos PIX/TED = Seg A). Confirmadas as posições que o parser lê:

| Campo | Posição | Confirmação no .RET real |
|---|---|---|
| Seu Número (chave de conciliação) | Seg A 74-93 | refs reais (`1784076905356001`, `PJ202606062-0004`, `19153326GD…`) |
| Nosso Número | Seg A 135-154 | `900106xxx` do banco |
| Data real efetivação | Seg A 155-162 | `15072026` |
| Valor real efetivação | Seg A 163-177 | soma dos 120 = **R$ 931.926,95** = somatória do trailer, ao centavo |
| Ocorrência | Seg A 231-232 | único campo que varia: `00` (117×), `PA` (1×), `PF` (2×) |
| Documento favorecido (fallback) | Seg B 19-32 | 82 capturados (subtipos 03/05); PIX-chave/telefone/email = zeros, ignorados |
| Autenticação | Seg Z 15-78 | 117 capturadas; 3 registros sem Seg Z |
| Somatória do lote | Trailer 5 24-41 | `000000000093192695` reconciliou |

**🔶 O que esse arquivo NÃO cobriu** (segue pendente de arquivo real): posições de **retorno** dos Seg J
(boleto) e Seg O (convênio) — o arquivo só tinha Seg A; e **códigos de rejeição** — todos os 120 liquidaram.
Códigos de ocorrência `PA`/`PF` (registros com movimento `333`, sem Seg Z) foram observados nesse arquivo
e hoje são classificados como **rejeição** (`SantanderRejectedCodes` — `PA` = "Pix não efetivado", `PF` =
"CPF/CNPJ do recebedor inconsistente"), liberando a parcela de volta para nova remessa (AEL-797).

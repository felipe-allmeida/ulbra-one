# Pergunta ao responsável do legado — pagamentos "externos" e órfãos

**Contexto:** estamos migrando o sistema **orcamento** (Firestore) para o **Ulbra One**. Cada solicitação vira uma *Requisition* e cada parcela paga (collection `pagamentos`) vira um *Payable* (Contas a Pagar) no One. Antes de importar a collection `pagamentos`, precisamos entender dois grupos de registros para decidir se entram e **como** entram no One.

Levantamento atual da collection `pagamentos`: **28.264 documentos** (1 por parcela), somando **R$ 170,7 mi** pagos.

---

## 1. Pagamentos "externos" (`isExternal = true`) — **85% do total (24.070 parcelas)**

A grande maioria dos pagamentos está marcada como `isExternal = true` e traz os campos `importId` e `importType`.

**Perguntas:**
1. **O que são esses pagamentos externos?** De onde eles vêm (planilha manual, exportação do Senior, retorno bancário, outro sistema)?
2. Os campos **`importId` / `importType`** identificam qual origem exatamente? Quais valores de `importType` existem e o que cada um significa?
3. **Como um pagamento externo aparece/se comporta no sistema legado** comparado a um do fluxo normal (que nasce de uma solicitação na esteira)? Ele aparece em Contas a Pagar? Tem solicitação associada de verdade ou é só um lançamento de pagamento?
4. Eles representam **obrigações reais já pagas** que precisam existir no One (para o Contábil/relatórios), ou são dados de apoio/conciliação que **não** deveriam virar título?

## 2. Estes externos = "baldes" `EXT_REQ_IMP_*` (mesmos registros)

Confirmamos que **todos** os 24.070 pagamentos `isExternal = true` pertencem a apenas **12 `requisitionId` sintéticos** com prefixo **`EXT_REQ_IMP_...`** (que **não existem** na collection `requisicoes`). Ou seja: "externos" e "órfãos" são o mesmo conjunto — grandes baldes de importação, somando **R$ 141,4 mi** (dos R$ 170,7 mi totais).

| `requisitionId` (balde) | parcelas | valor |
|---|---:|---:|
| `EXT_REQ_IMP_1787141012616_N8BRVY` | 8.066 | R$ 18.775.054,85 |
| `EXT_REQ_IMP_1782524709182_Y5ND62` | 7.422 | R$ 18.276.142,32 |
| `EXT_REQ_IMP_1784733571969_GCXZFZ` | 6.652 | R$ 15.373.846,43 |
| `EXT_REQ_IMP_1784898548220_3T43HG` | 398 | R$ 2.681.467,83 |
| `EXT_REQ_IMP_1787079329967_RWPCTD` | 330 | R$ 2.413.827,33 |
| `EXT_REQ_IMP_1785159927361_49J8MI` | 293 | R$ 17.678.876,71 |
| `EXT_REQ_IMP_1786120483963_GK30ZH` | 293 | R$ 18.104.532,92 |
| `EXT_REQ_IMP_1786120515043_5T62MO` | 293 | R$ 23.339.077,84 |
| `EXT_REQ_IMP_1785426126352_TPQCP7` | 293 | R$ 18.693.615,78 |
| `EXT_REQ_IMP_1782414039726_B4WDA3` | 24 | R$ 3.553.849,41 |
| `EXT_REQ_IMP_1782414415116_X4H85L` | 4 | R$ 1.799.744,83 |
| `EXT_REQ_IMP_1785444672464_K9H2HD` | 2 | R$ 690.022,99 |
| **Total** | **24.070** | **R$ 141.380.059,24** |

**Perguntas:**
5. O que cada balde `EXT_REQ_IMP_*` representa? (folha, guias/tributos em lote, importação do Senior, saldo inicial de Contas a Pagar, outro?) O sufixo/`importType` distingue a origem?
6. Devemos, no One, tratar cada balde como **um único título**, **um título por parcela** (viram milhares de títulos), ou **não importar** (é só histórico contábil)?
7. Esses valores **já entram no consumo do orçamento** no legado, ou correm por fora do orçamento?

---

## 3. Decisão atual (enquanto aguardamos)
- **Fase 1 importa apenas** `pagamentos` do fluxo (`isExternal != true`) com `requisitionId` válido.
- **Externos e órfãos ficam de fora** por enquanto.
- Dependendo das respostas, eles entram numa fase seguinte como Payable com origem **"importação"** (`Origin = SeniorImport`), com ou sem vínculo a uma Requisition.

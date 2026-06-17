---
name: criar-pr
description: Use ao criar um Pull Request neste projeto (Ulbra.One). Gera a descrição no template padrão do repositório e abre o PR via gh. Acione quando o usuário pedir para "criar PR", "abrir pull request" ou similar.
---

# Criar PR no padrão Ulbra.One

Gera a descrição de PR seguindo o template oficial (`.github/pull_request_template.md`) e abre o PR com `gh`.

## Passos

1. **Reúna o contexto da mudança** (não invente — derive do diff):
   - `git log main..HEAD --oneline` para ver os commits da branch.
   - `git diff main...HEAD --stat` para o escopo dos arquivos.
   - Identifique o número da issue do Linear a partir do nome da branch (`AEL-<n>/...`). Se não houver, registre "Sem issue" com justificativa.

2. **Escreva o corpo seguindo o template real** — `.github/pull_request_template.md` é a fonte da estrutura (leia-o; não re-invente seções). Em cada seção escreva conteúdo **derivado do diff**, nunca placeholder. Como preencher bem:

   - **O que é esse PR?** — 2 a 4 linhas: o *problema/porquê* e o que o PR *entrega*. O revisor entende o objetivo sem abrir o diff. Não repita o título nem liste arquivos.
   - **O que foi alterado?** — tabela, **uma linha por arquivo**; em PR grande (dezenas de arquivos) **agrupe por módulo/bloco** (uma linha por grupo coeso, com o caminho representativo) em vez de despejar tudo. Na coluna *Alteração* diga **o que mudou e por quê** ("FK de pagamento → `Restrict` pra travar edição com baixa"), nunca "atualizado". *Imagem* só pra mudança visual de UI (print/link); senão `-`.
   - **O que deve ser levado em consideração?** — a seção mais valiosa: o que o revisor **precisa saber pra não deixar passar nada**. Cubra mudanças de comportamento/contrato, **decisões de design e trade-offs (com o porquê)**, riscos/efeitos colaterais e o que ficou **fora de escopo / não coberto**. Não repita a lista de arquivos; só escreva "nada a considerar" se for genuinamente trivial.
   - **Issue** — link(s) do Linear (`AEL-<n>`); vários tickets → liste todos. Sem issue → `Sem issue: <motivo>`.
   - **Checklist** — copie o do template e **marque só o que você validou de fato**.

   Nível esperado (exemplo recortado):

   ```markdown
   ## O que é esse PR?
   Endurece a baixa de pagamento contra concorrência e edição indevida — dois riscos de dado
   financeiro: dois pagamentos simultâneos na mesma parcela, e editar um título já pago.

   ## O que foi alterado?
   | Nome do arquivo | Alteração | Imagem |
   |---|---|---|
   | `Payable.cs`, `Installment.cs`, `FinanceContext.cs` | xmin como token de concorrência (→409) + FK de pagamento `Restrict` | - |
   | `Endpoints/Payables/UpdatePayable/*.cs` | bloqueia edição quando há baixa (`Payable.HasPayments`) | - |

   ## O que deve ser levado em consideração?
   - **Comportamento:** writer concorrente recebe **409**; título com baixa não edita mais (teste de integração cobre os dois).
   - **Decisão:** xmin é coluna de sistema do Postgres → token de concorrência sem DDL.
   - **Fora de escopo:** retenção/expurgo, UI.
   ```

3. **Defina o título** no formato `tipo(AEL-<n>): descrição` (`feat`, `fix`, `chore`, `ci`, `refactor`, `docs`, `test`). **Sem issue, use o marcador explícito** `no-issue`: `tipo(no-issue): descrição`. O título é validado no CI pelo workflow `.github/workflows/pr-title.yml` — fora do padrão, o PR não passa.

4. **Verifique a regra de 1 migration por PR** antes de abrir:
   - `git diff main...HEAD --name-only | grep -E 'Migrations/.*\.cs$' | grep -v Designer | grep -v Snapshot` — conte os arquivos de migration. **Ideal: ≤ 1.** Se houver mais, avise o usuário (é a regra do time) e siga só com confirmação.

5. **Abra o PR (draft por padrão)**:
   - Garanta que a branch foi publicada (`git push -u origin <branch>` se necessário).
   - `gh pr create --draft --base main --head <branch> --title "<título>" --body "<corpo>"`.
   - Abra **sempre como draft**; o autor marca "ready for review" (`gh pr ready <n>`) quando estiver pronto pra revisão.

6. Retorne a URL do PR ao usuário (e lembre que está em draft).

## Regras

- Português em todo o conteúdo do PR.
- **Draft por padrão** — todo PR abre como rascunho.
- **No máximo 1 migration por PR** (snapshot junto). Mais que isso, sinalize.
- Não marque itens do checklist que você não validou.
- O corpo do PR deve espelhar as seções de `.github/pull_request_template.md` — se o template mudar, siga o template.

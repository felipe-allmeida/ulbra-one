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

2. **Escreva o corpo seguindo o template real** — `.github/pull_request_template.md` é a fonte da estrutura (leia-o; não re-invente seções). **O objetivo do corpo é um leitor humano entender o PR sem abrir o diff: o motivo, o que foi feito e o que mais for relevante.** Escreva de forma coesa, derivada do diff, nunca placeholder — não tente catalogar cada arquivo (o diff já faz isso). Como preencher bem:

   - **O que é esse PR?** — 2 a 4 linhas: o *problema/motivo* e o que o PR *entrega*. Não repita o título.
   - **O que foi alterado?** — uma narrativa curta e coesa (bullets) do **que foi feito e por quê**, agrupada por **mudança lógica / bloco**, não por arquivo (ex.: "concorrência otimista no Payable", "trilha de auditoria nova"). Não precisa ser exaustivo — o diff já tem os arquivos. Se houver mudança visual de UI, inclua um print/link junto do bullet.
   - **O que deve ser levado em consideração?** — a seção mais valiosa: o que o revisor **precisa saber pra não deixar passar nada**. Cubra mudanças de comportamento/contrato, **decisões de design e trade-offs (com o porquê)**, riscos/efeitos colaterais e o que ficou **fora de escopo / não coberto**. Só escreva "nada a considerar" se for genuinamente trivial.
   - **Issue** — link(s) do Linear (`AEL-<n>`); vários tickets → liste todos. Sem issue → `Sem issue: <motivo>`.
   - **Checklist** — copie o do template e **marque só o que você validou de fato**.

   Nível esperado (exemplo recortado — coeso, por mudança lógica, não por arquivo):

   ```markdown
   ## O que é esse PR?
   Endurece a baixa de pagamento contra concorrência e edição indevida — dois riscos de dado
   financeiro: dois pagamentos simultâneos na mesma parcela, e editar um título já pago.

   ## O que foi alterado?
   - **Concorrência otimista no Payable** (token `xmin`): um segundo writer simultâneo recebe 409 em vez de sobrescrever.
   - **Pagamentos viram append-only**: FK `Restrict` + bloqueio de edição quando o título já tem baixa.

   ## O que deve ser levado em consideração?
   - **Comportamento:** writer concorrente recebe **409**; título com baixa não edita mais (teste de integração cobre os dois).
   - **Decisão:** xmin é coluna de sistema do Postgres → token de concorrência sem DDL.
   - **Fora de escopo:** retenção/expurgo, UI.
   ```

3. **Mudanças de frontend (`web/`) → prints obrigatórios.** Todo PR que altera a UI leva screenshots das telas afetadas.
   - Suba o dev server (`pnpm --dir web dev --port 5199 --strictPort`) e rode `pnpm --dir web shots` (`web/scripts/pr-shots.mjs`). O script usa Playwright com o Chromium pré-instalado e **mocka `/auth/me` por cargo** (+ um catch-all), então captura login, estado "sem cargo" e o **menu por cargo sem depender do backend .NET**. Saída em `web/pr-shots/` (gitignored).
   - Para telas novas/diferentes, ajuste as rotas e os mocks no script antes de rodar.
   - **Anexe ao PR:** o repositório é privado — imagens não renderizam via URL externa/raw. Arraste os PNGs direto na descrição do PR no GitHub (upload nativo) e coloque cada print junto do bullet relevante em "O que foi alterado?". Operando headless (sem UI do GitHub), **envie os arquivos ao usuário** para ele anexar.

4. **Defina o título** no formato `tipo(AEL-<n>): descrição` (`feat`, `fix`, `chore`, `ci`, `refactor`, `docs`, `test`). **Sem issue, use o marcador explícito** `no-issue`: `tipo(no-issue): descrição`. O título é validado no CI pelo workflow `.github/workflows/pr-title.yml` — fora do padrão, o PR não passa.

5. **Verifique a regra de 1 migration por PR** antes de abrir:
   - `git diff main...HEAD --name-only | grep -E 'Migrations/.*\.cs$' | grep -v Designer | grep -v Snapshot` — conte os arquivos de migration. **Ideal: ≤ 1.** Se houver mais, avise o usuário (é a regra do time) e siga só com confirmação.

6. **Abra o PR (draft por padrão)**:
   - Garanta que a branch foi publicada (`git push -u origin <branch>` se necessário).
   - `gh pr create --draft --base main --head <branch> --title "<título>" --body "<corpo>"`.
   - Abra **sempre como draft**; o autor marca "ready for review" (`gh pr ready <n>`) quando estiver pronto pra revisão.

7. Retorne a URL do PR ao usuário (e lembre que está em draft).

## Regras

- Português em todo o conteúdo do PR.
- **Draft por padrão** — todo PR abre como rascunho.
- **No máximo 1 migration por PR** (snapshot junto). Mais que isso, sinalize.
- Não marque itens do checklist que você não validou.
- **Mudou frontend (`web/`) → sempre com prints** das telas afetadas (passo 3; `pnpm --dir web shots`).
- O corpo do PR deve espelhar as seções de `.github/pull_request_template.md` — se o template mudar, siga o template.

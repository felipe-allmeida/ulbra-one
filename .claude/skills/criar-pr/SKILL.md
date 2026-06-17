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

2. **Monte o corpo do PR** preenchendo EXATAMENTE estas seções, nesta ordem, em português:

   ```markdown
   ## O que é esse PR?

   <objetivo do PR em poucas linhas>

   ## O que foi alterado?

   | Nome do arquivo | Alteração | Imagem |
   |-----------------|-----------|--------|
   | <caminho>       | <o que mudou nesse arquivo> | <link/print quando houver, senão "-"> |

   ## O que deve ser levado em consideração?

   <pontos de atenção para o revisor: alteração na lógica de alguma feature,
   ponto mais importante do PR, impactos/riscos/efeitos colaterais>

   ## Issue

   <link do Linear AEL-<n> ou "Sem issue: <motivo>">

   ## Checklist

   - [ ] Título no padrão `tipo(AEL-<n>): descrição` (validado no CI `pr-title`).
   - [ ] No máximo 1 migration neste PR; snapshot commitado e `has-pending-model-changes` limpo.
   - [ ] Testes verdes: `dotnet test` (+ `pnpm --dir web checks`/`build` se mexeu no front).
   - [ ] Sem `DateTime.UtcNow` novo; endpoints usam `TypedResults.*`; Application não referencia Infrastructure.
   - [ ] PR aberto como **draft**.
   ```

   - Preencha a tabela com **um arquivo por linha** (use `git diff main...HEAD --stat` como base). Na coluna *Imagem*, coloque print/link só quando houver mudança visual; senão `-`. Em PRs grandes (dezenas de arquivos), agrupe por bloco/módulo em vez de listar cada arquivo.
   - **Marque no Checklist só o que você validou de fato.** Se a branch tiver **mais de 1 migration**, NÃO marque o item — sinalize ao usuário que o ideal é 1 migration por PR (regra do time) e siga só com aviso explícito.

3. **Defina o título** no formato `tipo(AEL-<n>): descrição` (`feat`, `fix`, `chore`, `ci`, `refactor`, `docs`, `test`). **Sem issue, use o marcador explícito** `no-issue`: `tipo(no-issue): descrição`. O título é validado no CI pelo workflow `.github/workflows/pr-title.yml` — fora do padrão, o PR não passa.

4. **Verifique a regra de 1 migration por PR** antes de abrir:
   - `git diff main...HEAD --name-only | grep -E 'Migrations/.*\.cs$' | grep -v Designer | grep -v Snapshot` — conte os arquivos de migration. **Ideal: ≤ 1.** Se houver mais, avise o usuário (é a regra do time) e siga só com confirmação.

5. **Abra o PR (draft por padrão)**:
   - Garanta que a branch foi publicada (`git push -u origin <branch>` se necessário).
   - `gh pr create --draft --base main --head <branch> --title "<título>" --body "<corpo>"`.
   - Abra **sempre como draft**; o autor marca "ready for review" (`gh pr ready <n>`) quando estiver pronto pra revisão.

6. Retorne a URL do PR ao usuário (e lembre que está em draft).

## Regras

- NÃO adicione linha `Co-Authored-By` nos commits nem no corpo do PR (preferência do time).
- Português em todo o conteúdo do PR.
- **Draft por padrão** — todo PR abre como rascunho.
- **No máximo 1 migration por PR** (snapshot junto). Mais que isso, sinalize.
- Não marque itens do checklist que você não validou.
- O corpo do PR deve espelhar as seções de `.github/pull_request_template.md` — se o template mudar, siga o template.

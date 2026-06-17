## O que é esse PR?

<!-- Descreva em poucas linhas o objetivo deste PR. -->

## O que foi alterado?

<!--
Resumo coeso do que foi feito e por quê, agrupado por mudança lógica (não por arquivo —
o diff já lista os arquivos). Escreva pra um humano entender sem abrir o diff.
-->

-

## O que deve ser levado em consideração?

<!--
Pontos de atenção para o revisor. Inclua, quando houver:
- Alteração na lógica de alguma feature
- Ponto mais importante do PR
- Impactos, riscos ou efeitos colaterais
-->

## Issue

<!-- Link do Linear (AEL-<n>) ou "Sem issue" com justificativa. -->

## Checklist

<!-- Marque só o que você de fato validou. -->
- [ ] Título no padrão `tipo(AEL-<n>): descrição` (ou `tipo(no-issue): …`) — validado no CI (`pr-title`).
- [ ] **No máximo 1 migration** neste PR; se houver, o `*ModelSnapshot.cs` foi commitado junto e `has-pending-model-changes` está limpo.
- [ ] Testes verdes: `dotnet test` (back) e — se mexeu no front — `pnpm --dir web checks` + `pnpm --dir web build`.
- [ ] Convenções: sem `DateTime.UtcNow` novo (use `TimeProvider`); endpoints retornam `TypedResults.*`; camada Application não referencia Infrastructure.
- [ ] PR aberto como **draft**; só marque "ready for review" quando estiver pronto pra revisão.

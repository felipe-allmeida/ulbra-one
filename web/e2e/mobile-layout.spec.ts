import type { Page } from '@playwright/test';
import { mobileTest as test, expect } from './helpers/mobile-fixtures';

/**
 * ===== O QUE ESTA TRAVA MEDE, E O QUE ELA NÃO MEDE =====
 *
 * Ela mede uma coisa só: se algum `LAYOUT_CONTAINER` (abaixo) tem `scrollWidth > clientWidth` —
 * ou seja, scroll horizontal *no nível da página* — em 390px e 768px, nas rotas em `ROUTES`. Um
 * run verde quer dizer exatamente isso e nada mais. Em particular, verde a 768px NÃO quer dizer
 * "a faixa tablet está coberta" — é a crença específica que esta nota existe para corrigir.
 *
 * `.rt-scroll` (Task 18 fix-round 1) está em `INTENTIONAL_SCROLL_SELECTOR` e nunca aparece em
 * `LAYOUT_CONTAINERS`: ele absorve de propósito o overflow de qualquer `ResponsiveTable` cuja
 * tabela desktop não caiba no `.panel`. Isso é correto — sem ele a tabela vazaria para `.content`
 * e faria a trava disparar por um motivo que o próprio `.rt-scroll` já resolve — mas o efeito
 * colateral é que nenhum `ResponsiveTable` pode fazer esta trava falhar a 768px de novo, não
 * importa quão mal ele renderize ali (colunas espremidas, texto cortado, sobreposição). A trava se
 * provou uma vez — quatro rotas falhavam a 768px antes do `.rt-scroll`, zero depois — e foi
 * desarmada logo em seguida para exatamente a classe de defeito para a qual ela foi criada.
 *
 * O fix do `.rt-scroll` está certo e fica. O que está errado é a crença que o número "17/17"
 * cria. Outros cinco pontos cegos conhecidos, para ficarem num lugar só que um mantenedor vai de
 * fato encontrar:
 *
 *   1. Sobreposição e espremimento de colunas — `scrollWidth <= clientWidth` não detecta um
 *      elemento visualmente esmagado ou dois elementos se sobrepondo; só detecta transbordo.
 *   2. Corte silencioso por `.panel { overflow: hidden }` — o próprio `.panel` some com o
 *      excesso sem deixar rastro de `scrollWidth`; ver o comentário de `LAYOUT_CONTAINERS` abaixo.
 *   3. Estados de loading e erro — `checkRoute` espera `networkidle` antes de medir, o que
 *      atravessa o branch de loading (skeleton, spinner) e nunca chega no branch de erro; nenhum
 *      dos dois é exercitado aqui.
 *   4. Cobertura de rotas — `ROUTES` aqui tem 8 entradas; `scripts/mobile-audit.mjs` varre 77.
 *      As outras ~69 nunca passam por este arquivo.
 *   5. A varredura (`scripts/mobile-audit.mjs`) usa o mesmo predicado `scrollWidth > clientWidth`
 *      que esta trava — logo "0 offenders" na varredura carrega exatamente a mesma ausência de
 *      informação sobre os 5 pontos acima. Ela amplia a cobertura de rotas (77 vs. 8), não os
 *      tipos de defeito detectados.
 *
 * Sete defeitos neste projeto foram achados por uma pessoa olhando a tela — nenhum por teste
 * automatizado algum. Um run verde aqui é evidência de "sem overflow de página", não de
 * "renderização mobile/tablet verificada".
 */

/** Contêineres que carregam a página inteira: nenhum pode precisar de scroll horizontal.
 * `.panel` entrou na lista na Task 4: ele tem `overflow: hidden` (src/index.css, para os cantos
 * arredondados) — uma tabela larga sem `.table-scroll`/`.rt-scroll` não "vaza" para `.content` nem
 * gera scroll visível algum, ela é silenciosamente CORTADA dentro do próprio `.panel` (colunas
 * somem, sem scrollbar, sem aviso). `.content`/`.panel` sozinhos passavam o teste trivialmente
 * porque cada um absorve o próprio estouro sem repassar `scrollWidth` para o pai — só ficou visível
 * ao rodar com dados reais (fornecedor/descrição longos), exatamente o buraco que esta task existe
 * pra fechar.
 *
 * `scripts/mobile-audit.mjs` (Task 18) duplica esta função e este array verbatim — não os importa,
 * porque aquele script é `.mjs` puro sem pipeline de TypeScript. Mudou um, muda o outro. */
const LAYOUT_CONTAINERS = ['.content', '.panel', '.topbar', '.drawer-body', '.modal-body'];

/** Contêineres que rolam de propósito — nunca são o "culpado" nem disparam a falha por si.
 * `.table-scroll`: as 4 planilhas contábeis (rola + primeira coluna fixa, ver index.css).
 * `.rt-scroll`: o wrapper do `<table>` desktop do ResponsiveTable (Task 18 fix-round 1) — reachability
 * pura (overflow-x: auto), sem a coluna fixa do `.table-scroll` (que mudaria a aparência de toda
 * tabela migrada, não só das 4 exceptuadas). */
const INTENTIONAL_SCROLL_SELECTOR = '.table-scroll, .rt-scroll';

export async function sidewaysScroll(page: Page) {
  return page.evaluate(
    ([selectors, intentional]) => {
      const out: string[] = [];
      for (const sel of selectors) {
        for (const box of document.querySelectorAll(sel)) {
          if (box.scrollWidth <= box.clientWidth) continue;
          const limit = box.getBoundingClientRect().right;
          // Aponta o culpado, senão a falha vira caça ao tesouro no CSS.
          const blame = [
            ...new Set(
              [...box.querySelectorAll('*')]
                .filter((el) => !el.closest(intentional)) // opt-in: rola de propósito
                .filter((el) => el.getBoundingClientRect().right > limit + 1)
                .map((el) => {
                  const cls = typeof el.className === 'string' ? el.className : '';
                  return `<${el.tagName.toLowerCase()} class="${cls}">`;
                }),
            ),
          ].slice(0, 4);
          out.push(
            `${sel} rola de lado (${box.scrollWidth}px em ${box.clientWidth}px) — culpados: ${blame.join(', ') || '?'}`,
          );
        }
      }
      return out;
    },
    [LAYOUT_CONTAINERS, INTENTIONAL_SCROLL_SELECTOR] as const,
  );
}

const ROUTES = [
  '/app/home',
  '/app/payables',
  '/app/payables/new',
  '/app/vendors',
  '/app/treasury',
  '/app/requisitions',
  '/app/accounting/trial-balance',
  '/app/settings/users',
];

/**
 * Lista de trabalho pendente da migração para `ResponsiveTable` (Fase 3-5): cada rota aqui tem uma
 * tabela larga (sem `.table-scroll`) que hoje estoura `.panel` a 390px — com o `.panel` de olho
 * (Task 4) isso agora falha de verdade, em vez de passar trivialmente contra um estado vazio.
 * Cada task de migração remove suas próprias rotas desta lista; a Task 18 exige que ela termine
 * vazia. Mantida como array simples e explícito (não calculada) de propósito — é o ponto de edição
 * óbvio para quem migrar a próxima tela.
 */
const AGUARDANDO_MIGRACAO: string[] = [];

async function checkRoute(page: Page, route: string) {
  await page.goto(route);
  await page.waitForLoadState('networkidle');
  const offenders = await sidewaysScroll(page);
  expect(offenders, `scroll horizontal acidental em ${route}:\n  ${offenders.join('\n  ')}`).toEqual([]);
}

// Task 18 fix-round 1: cobre também 768px (viewport `tabletPage`, ver mobile-fixtures.ts) — o card
// do ResponsiveTable só entra em cena até 640px, então de 641 a 1023px a tabela desktop inteira
// ficava sem nenhuma trava olhando para ela. A trava original só via 390px; isso a estende sem
// perder a cobertura de 390px que já tinha (mesmas rotas, mesmo AGUARDANDO_MIGRACAO).
for (const route of ROUTES) {
  // Rotas em AGUARDANDO_MIGRACAO: tabela larga, destravam conforme a migração da Fase 5 avança.
  // Remova a rota da lista ao migrar a tela — a marcação acima é a lista de trabalho pendente.
  // (`test.fixme` referenciado direto, não guardado numa variável: como método ele exige o `this`
  // do próprio `test`, e uma referência solta perde esse vínculo — só type-checa assim.)
  const mobileFn = async ({ mobilePage }: { mobilePage: Page }) => checkRoute(mobilePage, route);
  const tabletFn = async ({ tabletPage }: { tabletPage: Page }) => checkRoute(tabletPage, route);
  if (AGUARDANDO_MIGRACAO.includes(route)) {
    test.fixme(`${route} não rola de lado a 390px`, mobileFn);
    test.fixme(`${route} não rola de lado a 768px`, tabletFn);
  } else {
    test(`${route} não rola de lado a 390px`, mobileFn);
    test(`${route} não rola de lado a 768px`, tabletFn);
  }
}

test('o botão de menu é atingível ao toque', async ({ mobilePage: page }) => {
  await page.goto('/app/home');
  await page.waitForLoadState('networkidle');

  const box = await page.locator('.hamburger').boundingBox();
  expect(box, 'o hambúrguer não tem caixa — a navegação fica inalcançável no celular').not.toBeNull();
  expect(box!.width, 'largura do alvo de toque').toBeGreaterThanOrEqual(44);
  expect(box!.height, 'altura do alvo de toque').toBeGreaterThanOrEqual(44);

  // Estar visível não basta: outro elemento pode cobri-lo. Precisa RECEBER o toque.
  const hits = await page.evaluate(() => {
    const b = document.querySelector('.hamburger')!.getBoundingClientRect();
    return document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2)?.closest('.hamburger') !== null;
  });
  expect(hits, 'o centro do hambúrguer não recebe o toque').toBe(true);

  await page.locator('.hamburger').click();
  await expect(page.locator('.side-nav')).toHaveClass(/open/);
});

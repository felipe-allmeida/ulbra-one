import { test, expect, type BrowserContext, type Page } from '@playwright/test';
import { seedContext } from './helpers/mobile-fixtures';

/**
 * A preferência de recolher/redimensionar a navegação existe só acima de 1024px. Quem a neutraliza
 * abaixo disso é o CSS (`@media (max-width: 1024px)` em index.css), não um breakpoint duplicado em
 * JS — o componente sempre emite a classe e a custom property, e a folha de estilos vence.
 *
 * É por isso que esta trava é E2E e não unitária: jsdom não aplica CSS, então nenhum teste do
 * `SideNav.test.tsx` consegue ver a neutralização acontecer. Sem este arquivo, alguém pode remover
 * as regras do bloco de 1024px e todos os testes unitários continuam verdes enquanto o drawer do
 * tablet passa a abrir recolhido e inútil.
 */

/** A preferência mais hostil possível: recolhida e no mínimo. */
const seedPreference = async (ctx: BrowserContext) => {
  await seedContext(ctx);
  await ctx.addInitScript(() => {
    localStorage.setItem('ulbra-one.nav.collapsed', 'true');
    localStorage.setItem('ulbra-one.nav.width', '180');
  });
};

const probe = (page: Page) =>
  page.evaluate(() => {
    const shown = (sel: string) => {
      const el = document.querySelector(sel);
      return el instanceof HTMLElement ? getComputedStyle(el).display !== 'none' : false;
    };
    const panel = document.querySelector('.nav-panel');
    return {
      panelShown: shown('.nav-panel'),
      panelWidth: panel ? Math.round(panel.getBoundingClientRect().width) : null,
      handleShown: shown('.nav-resize'),
      toggleShown: shown('.nav-collapse-btn'),
    };
  });

test('a 768px o drawer ignora a preferência de desktop', async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 768, height: 1024 } });
  await seedPreference(ctx);
  const page = await ctx.newPage();
  await page.goto('/app/payables');
  await page.waitForLoadState('networkidle');
  await page.locator('.hamburger').click();

  const r = await probe(page);
  expect(r.panelShown, 'o drawer do tablet abriria recolhido e sem itens').toBe(true);
  expect(r.panelWidth, 'a largura de desktop não pode vazar para o drawer').toBe(225);
  expect(r.handleShown, 'não há o que redimensionar num drawer').toBe(false);
  expect(r.toggleShown, 'recolher não faz sentido num drawer').toBe(false);

  await ctx.close();
});

test('a 1280px a preferência vale', async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 860 } });
  await seedPreference(ctx);
  const page = await ctx.newPage();
  await page.goto('/app/payables');
  await page.waitForLoadState('networkidle');

  const r = await probe(page);
  expect(r.panelShown).toBe(false);
  expect(r.handleShown).toBe(true);
  expect(r.toggleShown).toBe(true);
  // Recolhida é o rail, não largura zero: trocar de domínio continua possível sem expandir.
  expect(await page.locator('.side-nav').boundingBox()).toMatchObject({ width: 82 });
  expect(await page.locator('.nav-rail-btn').count()).toBeGreaterThan(1);

  await ctx.close();
});

test('sem preferência salva a sidebar mede os 307px de sempre', async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 860 } });
  await seedContext(ctx);
  const page = await ctx.newPage();
  await page.goto('/app/payables');
  await page.waitForLoadState('networkidle');

  expect(await page.locator('.side-nav').boundingBox()).toMatchObject({ width: 307 });
  await ctx.close();
});

test('arrastar abaixo do mínimo recolhe, em vez de travar na borda', async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 860 } });
  await seedContext(ctx);
  const page = await ctx.newPage();
  await page.goto('/app/payables');
  await page.waitForLoadState('networkidle');

  const box = (await page.locator('.nav-resize').boundingBox())!;
  await page.mouse.move(box.x + 2, box.y + 300);
  await page.mouse.down();
  await page.mouse.move(box.x - 300, box.y + 300, { steps: 10 });
  await page.mouse.up();

  await expect(page.locator('.side-nav')).toHaveClass(/nav-collapsed/);
  expect(await page.locator('.side-nav').boundingBox()).toMatchObject({ width: 82 });

  await ctx.close();
});

test('a preferência sobrevive ao reload', async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 860 } });
  await seedContext(ctx);
  const page = await ctx.newPage();
  await page.goto('/app/payables');
  await page.waitForLoadState('networkidle');

  await page.locator('.nav-collapse-btn').click();
  await expect(page.locator('.side-nav')).toHaveClass(/nav-collapsed/);

  await page.reload();
  await page.waitForLoadState('networkidle');
  await expect(page.locator('.side-nav')).toHaveClass(/nav-collapsed/);

  await ctx.close();
});

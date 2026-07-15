// PR screenshots — boots against a running vite dev server and mocks the API
// (/auth/me per cargo + an empty-array catch-all) so authenticated views render
// without the .NET backend. Usage: `node scripts/pr-shots.mjs` (vite on :5199).
import { chromium } from '@playwright/test';
import { mkdirSync } from 'node:fs';

const BASE = process.env.SHOT_BASE ?? 'http://localhost:5199';
const OUT = process.env.SHOT_OUT ?? 'pr-shots'; // gitignored; anexe estes PNGs ao PR
mkdirSync(OUT, { recursive: true });

const PERMS = {
  Finance: [
    'payables:read',
    'payables:write',
    'payables:pay',
    'receivables:read',
    'receivables:write',
    'receivables:receive',
    'treasury:read',
    'treasury:write',
    'registry:read',
    'registry:write',
    'core:read',
    'reports:read',
  ],
  Controllership: [
    'payables:read',
    'payables:approve',
    'receivables:read',
    'treasury:read',
    'treasury:approve',
    'accounting:read',
    'fiscal:read',
    'registry:read',
    'core:read',
    'core:write',
    'reports:read',
    'audit:read',
  ],
  Accounting: [
    'accounting:read',
    'accounting:write',
    'accounting:post',
    'fiscal:read',
    'fiscal:write',
    'reports:read',
    'payables:read',
    'receivables:read',
    'registry:read',
    'core:read',
  ],
  Admin: [],
};
const meFor = (role) => ({
  userId: 'user_00000000000000000000000000000001',
  name: role === 'User' ? 'Novo Usuário' : `${role} Dev`,
  email: `${role === 'User' ? 'novo' : role.toLowerCase()}@ulbra.local`,
  role,
  isAdmin: role === 'Admin',
  permissions: PERMS[role] ?? [],
});

// External hosts (fonts/GIS/clarity) would hang networkidle — abort them.
async function blockExternal(ctx) {
  await ctx.route(/googleapis|gstatic|accounts\.google|clarity\.ms/, (r) => r.abort());
}
async function mockApi(ctx, role) {
  await ctx.route('**/api/**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await ctx.route('**/auth/me', (r) =>
    r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(meFor(role)) }),
  );
}
const shot = async (page, name) => {
  await page.screenshot({ path: `${OUT}/${name}.png` });
  console.log('saved', name);
};

const browser = await chromium.launch({
  executablePath: process.env.PW_CHROME ?? '/opt/pw-browsers/chromium',
  headless: true,
});

// 1) Login (dev) — botões por cargo (estático).
{
  const ctx = await browser.newContext({ viewport: { width: 1180, height: 1024 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  const page = await ctx.newPage();
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Entrar como Admin')
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(400);
  await shot(page, '01-login-dev');
  await ctx.close();
}

// 2) Estado "sem cargo".
{
  const ctx = await browser.newContext({ viewport: { width: 1180, height: 820 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(() => localStorage.setItem('ulbra-one.token', 'pr-shots'));
  await mockApi(ctx, 'User');
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText(/acesso está sendo configurado/i)
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(400);
  await shot(page, '02-sem-cargo');
  await ctx.close();
}

// 3) Menu por cargo — navega a /app/home (o SideNav difere por permissão).
for (const role of ['Finance', 'Controllership', 'Accounting', 'Admin']) {
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 900 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(() => localStorage.setItem('ulbra-one.token', 'pr-shots'));
  await mockApi(ctx, role);
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/home`, { waitUntil: 'domcontentloaded' });
  await page
    .locator('aside.side-nav')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(800);
  await shot(page, `03-menu-${role.toLowerCase()}`);
  await ctx.close();
}

// 4) Tela "Cargos & permissões" (Admin) — matriz cargo × permissão.
{
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 1000 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(() => localStorage.setItem('ulbra-one.token', 'pr-shots'));
  await mockApi(ctx, 'Admin');
  // Rotas específicas registradas por último têm precedência sobre o catch-all `[]`.
  await ctx.route('**/api/permissions', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        'core:read',
        'core:write',
        'registry:read',
        'registry:write',
        'payables:read',
        'payables:write',
        'payables:approve',
        'payables:pay',
        'receivables:read',
        'receivables:write',
        'receivables:receive',
        'treasury:read',
        'treasury:write',
        'treasury:approve',
        'accounting:read',
        'accounting:write',
        'accounting:post',
        'fiscal:read',
        'fiscal:write',
        'reports:read',
        'admin:users',
        'audit:read',
      ]),
    }),
  );
  await ctx.route('**/api/roles', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 'role_admin', name: 'Admin', permissions: [] },
        {
          id: 'role_finance',
          name: 'Finance',
          permissions: ['payables:read', 'payables:write', 'payables:pay', 'receivables:read', 'treasury:read'],
        },
        {
          id: 'role_controllership',
          name: 'Controllership',
          permissions: ['payables:read', 'payables:approve', 'treasury:approve', 'accounting:read', 'audit:read'],
        },
        {
          id: 'role_accounting',
          name: 'Accounting',
          permissions: ['accounting:read', 'accounting:write', 'accounting:post', 'fiscal:read'],
        },
        { id: 'role_user', name: 'User', permissions: [] },
      ]),
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/settings/roles`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Matriz de acesso')
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(600);
  await shot(page, '04-cargos-permissoes');
  await ctx.close();
}

await browser.close();
console.log('done →', OUT);

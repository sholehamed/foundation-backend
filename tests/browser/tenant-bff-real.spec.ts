import { expect, test, type Page, type Browser } from '@playwright/test';

const tenantHost = 'https://e2e-http-ci.shinera.localhost:8443';
const identityHost = 'https://identity.shinera.localhost:8443';
const secondTenantHost = 'https://other.shinera.localhost:8443';
const password = process.env.SHINERA_DEVELOPMENT_ADMIN_PASSWORD;
const dynamicNavigationPilot = process.env.FR15D_E2E_DYNAMIC_NAVIGATION === 'true';

function flattenMenuRoutes(categories: any[]): string[] {
  const result: string[] = [];
  const scan = (items: any[]) => {
    for (const item of items) {
      if (item.route) result.push(item.route);
      if (Array.isArray(item.childs)) scan(item.childs);
    }
  };
  for (const category of categories) scan(category.items ?? []);
  return result;
}

interface ApiResult {
  status: number;
  data: any;
}

async function api(page: Page, method: string, path: string, body?: unknown,
                   csrf: boolean = false): Promise<ApiResult> {
  return await page.evaluate(async ({ method, path, body, csrf }) => {
    const headers: Record<string, string> = {};
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (csrf) {
      const bootstrap = await fetch('/bff/auth/csrf', {
        credentials: 'same-origin',
      });
      if (bootstrap.status !== 204)
        throw new Error('BFF anti-forgery bootstrap returned ' + bootstrap.status);
      const value = document.cookie.split(';').map(x => x.trim())
        .find(x => x.startsWith('XSRF-TOKEN='));
      if (!value) throw new Error('No readable XSRF-TOKEN request cookie');
      headers['X-XSRF-TOKEN'] =
        decodeURIComponent(value.slice('XSRF-TOKEN='.length));
    }
    const response = await fetch(path, {
      method, credentials: 'same-origin', headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    let data: any = null;
    if (text) {
      try { data = JSON.parse(text); } catch { data = text; }
    }
    return { status: response.status, data };
  }, { method, path, body, csrf });
}

async function login(page: Page, username: string, targetPath = '/dashboard/members'): Promise<void> {
  // Log only endpoint path + response code; never log credentials,
  // authorization parameters, cookies, code, code_verifier or state.
  page.on('response', response => {
    try {
      const url = new URL(response.url());
      const path = url.pathname.toLowerCase();
      if (path === '/api/system/auth/interactive-login' ||
          path === '/api/connect/authorize' ||
          path === '/signin-oidc' ||
          path === '/bff/session' ||
          path === '/bff/auth/login' ||
          path === '/api/system/auth/csrf') {
        console.log('FR12G-C auth HTTP: ' +
          url.hostname + path + ' -> ' + response.status());
      }
    } catch {
      // Ignore non-HTTP navigation events.
    }
  });
  page.on('framenavigated', frame => {
    if (frame !== page.mainFrame()) return;
    try {
      const url = new URL(frame.url());
      console.log('FR12G-C browser path: ' + url.hostname + url.pathname);
    } catch {
      // Ignore temporary about:blank documents.
    }
  });
  console.log('FR12G-C stage: opening Tenant dashboard');
  await page.goto(tenantHost + targetPath, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await expect(page).toHaveURL(/\/auth\/login/, { timeout: 30_000 });
  console.log('FR12G-C stage: Tenant redirect to Identity via BFF');
  await page.getByRole('button', { name: /ورود امن از طریق سرویس هویت/ }).click();

  // This is the REAL OpenIddict authorization redirect through the
  // Identity hostname, not a mocked HTTP fixture or token injection.
  await expect(page).toHaveURL(
    /identity\.shinera\.localhost:8443\/auth\/login/,
    { timeout: 30_000 },
  );
  console.log('FR12G-C stage: Identity redirect arrived; checking interactive login');
  const fields = page.locator('app-custom-input input');
  await expect(fields).toHaveCount(2);
  console.log('FR12G-C stage: Identity form visible, filling fixture credentials');
  await fields.nth(0).fill(username);
  await fields.nth(1).fill(password!);
  await page.locator('#captcha-code').fill('ABCDE');
  console.log('FR12G-C stage: submitting Identity interactive login');
  // Angular submits the form and then assigns a new browser URL on success.
  // Do not let Playwright's implicit full-navigation wait hide the actual
  // HTTP/redirect failure behind the test-wide timeout.
  // The visual icon inside the login submit button contributes its ligature
  // text to the accessible name. Select by form role, not exact label text.
  await page.locator('form button[type="submit"]')
    .click({ noWaitAfter: true, timeout: 10000 });
  console.log('FR12G-C stage: form submitted; checking live OIDC callback');
  await expect.poll(() => {
    try {
      const url = new URL(page.url());
      return url.host + url.pathname; // never print code/state/query tokens
    } catch {
      return 'unavailable';
    }
  }, { timeout: 45_000, intervals: [300, 500, 1000] })
    .toBe('e2e-http-ci.shinera.localhost:8443' + targetPath);
  console.log('FR12G-C stage: returned to Tenant dashboard');
}

test('real Chromium: OIDC PKCE -> host-only BFF cookie -> live Tenant permissions and revocation', async ({
  browser,
}: { browser: Browser }) => {
  expect(password?.length).toBeGreaterThanOrEqual(12);
  const ownerContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const staffContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const owner = await ownerContext.newPage();
  const staff = await staffContext.newPage();

  const ownerRequests: string[] = [];
  owner.on('request', request => {
    if (request.url().includes('/api/tenant/workspace') &&
        request.headers()['authorization']) ownerRequests.push(request.method());
  });

  try {
    console.log('FR12G-C stage: owner login starts');
    await login(owner, 'e2e-tenant');
    console.log('FR12G-C stage: owner login completed');
    const cookies = await ownerContext.cookies(tenantHost);
    const ticket = cookies.find(c => c.name === '__Host-Foundation.Bff');
    expect(ticket, 'Real secure BFF cookie must be present').toBeDefined();
    expect(ticket!.secure).toBe(true);
    expect(ticket!.httpOnly).toBe(true);
    expect(ticket!.domain).toBe('e2e-http-ci.shinera.localhost');

    console.log('FR12G-C stage: validating Owner BFF session');
    const live = await api(owner, 'GET', '/bff/session');
    expect(live.status).toBe(200);
    expect(live.data.authenticated).toBe(true);
    expect(live.data.context.type).toBe('tenant');
    expect(live.data.context.tenantSlug).toBe('e2e-http-ci');
    expect(ownerRequests).toEqual([]);

    if (dynamicNavigationPilot) {
      // Full compiled Angular + real BFF + SQL, not mocked navigation JSON.
      // Only the trusted disposable CI test may enable this pilot.
      const result = await api(owner, 'GET', '/api/tenant/workspace/navigation/me');
      expect(result.status).toBe(200);
      const routes = flattenMenuRoutes(result.data);
      // The disposable Owner must mirror FR-16 real signup: 17 scoped
      // RolePermissions, so ALL six globally owned Tenant menus are visible.
      // Never water down this assertion to the 3 ungated legacy fallback
      // links; the fixture must be fixed instead of hiding missing grants.
      // There must also be no duplicate CI-created navigation rows.
      expect(new Set(routes).size, 'First-install menu roots are unique').toBe(routes.length);
      expect(new Set(routes), 'First-install catalog must be exact, not an incomplete seeded menu')
        .toEqual(new Set([
          '/dashboard', '/dashboard/business', '/dashboard/branches',
          '/dashboard/members', '/dashboard/roles', '/dashboard/groups',
        ]));
      expect(routes.filter(route => route === '/dashboard')).toHaveLength(1);
      for (const route of ['/dashboard', '/dashboard/business', '/dashboard/branches'])
        expect(routes, 'System-owned canonical Tenant root').toContain(route);
      expect(routes.every(route => route.startsWith('/dashboard'))).toBe(true);
      expect(routes).not.toContain('/system/roles');
      const nav = owner.locator('nav[aria-label="ناوبری فضای کاری"]');
      for (const route of ['/dashboard', '/dashboard/business', '/dashboard/branches'])
        await expect(nav.locator('a[href="' + route + '"]')).toHaveCount(1);
      console.log('FR15D: Angular uses real centrally authored Tenant Menus, no static duplicate');
    }

    const storage = await owner.evaluate(() => ({
      access: localStorage.getItem('access_token'),
      refresh: localStorage.getItem('refresh_token'),
    }));
    expect(storage).toEqual({ access: null, refresh: null });

    const crossTenant = await ownerContext.request.get(
      secondTenantHost + '/bff/session');
    expect([401, 403]).toContain(crossTenant.status());
    expect((await ownerContext.cookies(secondTenantHost)).some(
      c => c.name === '__Host-Foundation.Bff')).toBe(false);

    // An authenticated Tenant Owner's BFF cookie MUST NOT grant access to
    // System Admin's central registry, even when the browser uses the
    // same-origin BFF bridge instead of a directly supplied Bearer JWT.
    for (const route of [
      '/api/system/FeatureRegistry/tenant-permissions/preview',
      '/api/system/FeatureRegistry/tenant-navigation/readiness',
      '/api/system/FeatureRegistry/system-endpoints/integrity',
      '/api/system/FeatureRegistry/central-access/readiness',
    ]) {
      const forbiddenSystemCatalog = await api(owner, 'GET', route);
      // BFF may reject a Tenant-context cookie with 401 before reaching
      // SystemAdministrator, or the policy may return 403 for a valid
      // principal. Both MUST deny; a 200 would be a security regression.
      expect([401, 403], route + ' must remain System-only').toContain(
        forbiddenSystemCatalog.status);
    }

    // The other Tenant hostname must reject this Owner's BFF cookie for
    // protected resource reads; no cross-host session fallback is allowed.
    const wrongTenantMembers = await ownerContext.request.get(
      secondTenantHost + '/api/tenant/workspace/members');
    expect([401, 403]).toContain(wrongTenantMembers.status());

    console.log('FR12G-C stage: reading Tenant Members via BFF Cookie');
    const members = await api(owner, 'GET', '/api/tenant/workspace/members');
    expect(members.status).toBe(200);
    const staffRecord = members.data.items.find(
      (x: any) => x.userName === 'e2e-iam-staff');
    expect(staffRecord?.id).toBeTruthy();

    // A valid BFF ticket cannot bypass origin+antiforgery requirements.
    const raw = await api(owner, 'POST', '/api/tenant/workspace/roles', {
      name: 'Browser CSRF denied', description: null,
    });
    expect(raw.status).toBe(400);

    console.log('FR12G-C stage: creating audited Tenant role with CSRF');
    const created = await api(owner, 'POST', '/api/tenant/workspace/roles', {
      name: 'Browser BFF Reception', description: 'Real browser OIDC fixture',
    }, true);
    expect(created.status).toBe(200);
    const roleId = created.data.id as string;
    expect(roleId).toBeTruthy();

    const details = await api(owner, 'GET',
      '/api/tenant/workspace/roles/' + roleId + '/permissions');
    expect(details.status).toBe(200);
    const granted = await api(owner, 'PUT',
      '/api/tenant/workspace/roles/' + roleId + '/permissions', {
        expectedVersion: details.data.version,
        codes: ['TENANT.MEMBERS.READ'],
      }, true);
    expect(granted.status).toBe(200);

    const assignments = await api(owner, 'GET',
      '/api/tenant/workspace/members/' + staffRecord.id + '/roles');
    expect(assignments.status).toBe(200);
    const assigned = await api(owner, 'PUT',
      '/api/tenant/workspace/members/' + staffRecord.id + '/roles', {
        expectedVersion: assignments.data.version,
        roleIds: [roleId],
      }, true);
    expect(assigned.status).toBe(200);

    console.log('FR12G-C stage: staff login starts');
    await login(staff, 'e2e-iam-staff');
    console.log('FR12G-C stage: staff login completed');
    const staffAccess = await api(staff, 'GET', '/api/tenant/workspace/members');
    expect(staffAccess.status).toBe(200);
    const staffBffSession = await api(staff, 'GET', '/bff/session');
    expect(staffBffSession.status).toBe(200);

    if (dynamicNavigationPilot) {
      const staffNav = await api(staff, 'GET', '/api/tenant/workspace/navigation/me');
      expect(staffNav.status).toBe(200);
      const routes = flattenMenuRoutes(staffNav.data);
      expect(routes).toContain('/dashboard/members');
      expect(routes).not.toContain('/dashboard/roles');
      expect(routes).not.toContain('/dashboard/groups');
      const nav = staff.locator('nav[aria-label="ناوبری فضای کاری"]');
      await expect(nav.locator('a[href="/dashboard/members"]')).toHaveCount(1);
      await expect(nav.locator('a[href="/dashboard/roles"]')).toHaveCount(0);
      await expect(nav.locator('a[href="/dashboard/groups"]')).toHaveCount(0);
      console.log('FR15D: staff sees Members link only after real role grant');
    }

    const current = await api(owner, 'GET',
      '/api/tenant/workspace/roles/' + roleId + '/permissions');
    const revoked = await api(owner, 'PUT',
      '/api/tenant/workspace/roles/' + roleId + '/permissions', {
        expectedVersion: current.data.version,
        codes: [],
      }, true);
    expect(revoked.status).toBe(200);

    // IdentityBffSessionAccessValidator deliberately invalidates a previously
    // issued BFF ticket whenever its role/permission fingerprint changes.
    // The old cookie must be rejected immediately; do NOT weaken this to
    // keeping a stale browser principal authenticated.
    console.log('FR12G-C stage: verifying stale Staff BFF Cookie revocation');
    const staleAccess = await api(staff, 'GET', '/api/tenant/workspace/members');
    expect(staleAccess.status).toBe(401);
    const staleSession = await api(staff, 'GET', '/bff/session');
    expect(staleSession.status).toBe(401);

    // Clear both app and Identity-host cookies to force fresh Code+PKCE login.
    // With a newly issued valid ticket, the live Tenant permission evaluator
    // must independently deny the now-revoked permission with HTTP 403.
    // A valid BFF Cookie must NOT be challenged as a missing Bearer token.
    await staffContext.clearCookies();
    console.log('FR12G-C stage: Staff re-login after grant fingerprint revocation');
    await login(staff, 'e2e-iam-staff', '/dashboard');
    expect((await api(staff, 'GET', '/bff/session')).status).toBe(200);
    const forbidden = await api(staff, 'GET', '/api/tenant/workspace/members');
    expect(forbidden.status).toBe(403);
    if (dynamicNavigationPilot) {
      const updated = await api(staff, 'GET', '/api/tenant/workspace/navigation/me');
      expect(updated.status).toBe(200);
      expect(flattenMenuRoutes(updated.data)).not.toContain('/dashboard/members');
      const nav = staff.locator('nav[aria-label="ناوبری فضای کاری"]');
      await expect(nav.locator('a[href="/dashboard/members"]')).toHaveCount(0);
      await expect(nav.locator('a[href="/dashboard"]')).toHaveCount(1);
      console.log('FR15D: fresh staff session has NO revoked Member link, API remains 403');
    }

    console.log('FR12G-C stage: revoking Owner BFF Cookie ticket');
    const ownerLogout = await api(owner, 'POST', '/bff/auth/logout', {}, true);
    expect(ownerLogout.status).toBe(204);
    expect((await api(owner, 'GET', '/bff/session')).status).toBe(401);
    expect(ownerRequests).toEqual([]);
  } finally {
    await staffContext.close();
    await ownerContext.close();
  }
});

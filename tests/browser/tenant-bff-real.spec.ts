import { expect, test, type Page, type Browser } from '@playwright/test';

const tenantHost = 'https://e2e-http-ci.shinera.localhost:8443';
const identityHost = 'https://identity.shinera.localhost:8443';
const secondTenantHost = 'https://other.shinera.localhost:8443';
const password = process.env.SHINERA_DEVELOPMENT_ADMIN_PASSWORD;

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
    await staffContext.clearCookies();
    console.log('FR12G-C stage: Staff re-login after grant fingerprint revocation');
    await login(staff, 'e2e-iam-staff', '/dashboard');
    expect((await api(staff, 'GET', '/bff/session')).status).toBe(200);
    const forbidden = await api(staff, 'GET', '/api/tenant/workspace/members');
    expect(forbidden.status).toBe(403);

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

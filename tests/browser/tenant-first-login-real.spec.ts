import { expect, test, type Page, type Browser } from '@playwright/test';

const tenantHost = 'https://e2e-http-ci.shinera.localhost:8443';
const identityHost = 'https://identity.shinera.localhost:8443';
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

async function login(page: Page, username: string, targetPath = '/dashboard/members', userPassword = password!): Promise<void> {
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
  await fields.nth(1).fill(userPassword);
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


// This exercises the REAL Owner + Angular + HTTPS Identity-host + SQL + OIDC
// path, not mock network fixtures. Never trace/screenshot this scenario:
// one-time credentials are visible to the Owner and entered by the staff.
test.use({ trace: 'off', screenshot: 'off', video: 'off' });

test.describe('FR-13B-3A/3C: email-less staff onboarding through BFF', () => {

  test('Owner creates staff -> normal login forces password change -> fresh BFF login -> grant/revoke', async ({
    browser,
  }: { browser: Browser }) => {
    test.setTimeout(240_000);
    const ownerContext = await browser.newContext({ ignoreHTTPSErrors: true });
    const staffContext = await browser.newContext({ ignoreHTTPSErrors: true });
    const owner = await ownerContext.newPage();
    const staff = await staffContext.newPage();
    const suffix = String(Date.now() % 100000000);
    const username = 'e2e-first-' + suffix;
    // Disposable CI-only new password; no pre-set shared production password.
    const newPassword = 'E2e-Test-Unique-Passphrase-2026!' + suffix;
    const tenantApi = '/api/tenant/workspace';
    try {
      await login(owner, 'e2e-tenant');
      const caps = await api(owner, 'GET', tenantApi + '/admin-capabilities');
      expect(caps.status).toBe(200);
      expect(caps.data.canViewMembers).toBe(true);
      expect(caps.data.canCreateMembers).toBe(true);
      expect(caps.data.canIssueTemporaryPassword).toBe(true);

      // Exercise the actual UI and backend ATOMIC write with genuinely NULL email.
      await owner.getByRole('button', { name: 'ثبت عضو غیرفعال' }).click();
      const create = owner.locator('form.tenant-member-create-form');
      await create.locator('input[name="userName"]').fill(username);
      await create.locator('input[name="firstName"]').fill('E2E');
      await create.locator('input[name="lastName"]').fill('FirstLogin');
      await expect(create.locator('input[name="email"]')).toHaveValue('');
      await expect(create.locator('input[type="checkbox"]')).toBeChecked();

      const initialResponse = owner.waitForResponse(response =>
        new URL(response.url()).pathname === tenantApi + '/members/with-initial-password' &&
        response.request().method() === 'POST',
      );
      await owner.getByRole('button', { name: 'ثبت کارمند و صدور رمز اولیه' }).click();
      const response = await initialResponse;
      expect(response.status()).toBe(200);
      const result = await response.json();
      expect(result.member.userName).toBe(username);
      expect(result.member.email).toBeNull();
      expect(result.member.isActive).toBe(false);
      const initialPassword: string = result.temporaryPassword;
      expect(/^[A-Za-z0-9]{6}(?:-[A-Za-z0-9]{6}){3}$/.test(initialPassword)).toBe(true);
      const secretDialog = owner.getByRole('dialog', { name: 'رمز اولیه یک‌بارنمایش' });
      await expect(secretDialog).toBeVisible();
      expect((await secretDialog.locator('code').innerText()) === initialPassword).toBe(true);
      await secretDialog.getByRole('button', { name: /تحویل دادم/ }).click();
      await expect(secretDialog).toHaveCount(0);

      // No staff login, BFF cookie or role exists before proof of the secret.
      expect((await staffContext.cookies(tenantHost))
        .some(cookie => cookie.name === '__Host-Foundation.Bff')).toBe(false);
      expect((await staffContext.request.get(tenantHost + '/bff/session')).status()).toBe(401);
      const rolesBefore = await api(owner, 'GET',
        tenantApi + '/members/' + result.member.id + '/roles');
      expect(rolesBefore.status).toBe(200);
      expect(rolesBefore.data.roles.some((r: any) => r.assigned)).toBe(false);

      // The normal Tenant login button starts REAL Code+PKCE on Identity host.
      // The employee is NOT sent to a separate "first login" route.
      await staff.goto(tenantHost + '/dashboard', { waitUntil: 'domcontentloaded' });
      await expect(staff).toHaveURL(/\/auth\/login/);
      await staff.getByRole('button', { name: /ورود امن از طریق سرویس هویت/ }).click();
      await expect(staff).toHaveURL(
        /identity\.shinera\.localhost:8443\/auth\/login/, { timeout: 30_000 });
      const loginFields = staff.locator('app-custom-input input');
      await expect(loginFields).toHaveCount(2);
      await loginFields.nth(0).fill(username);
      await loginFields.nth(1).fill(initialPassword);
      await staff.locator('#captcha-code').fill('ABCDE');
      await staff.locator('form button[type="submit"]').click({ noWaitAfter: true });
      await expect(staff.getByRole('heading', { name: 'تغییر اجباری رمز عبور' }))
        .toBeVisible({ timeout: 30_000 });
      expect((await staffContext.cookies(identityHost))
        .some(cookie => cookie.name === '__Host-Foundation.Identity')).toBe(false);
      expect((await staffContext.cookies(tenantHost))
        .some(cookie => cookie.name === '__Host-Foundation.Bff')).toBe(false);
      expect((await staffContext.request.get(tenantHost + '/bff/session')).status()).toBe(401);

      // A forged completion without antiforgery must be rejected.
      const missingCsrf = await staff.evaluate(async () => {
        const response = await fetch('/api/system/Auth/interactive-first-login/complete', {
          method: 'POST',
          credentials: 'same-origin',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            tenantSlug: 'e2e-http-ci',
            challenge: '0'.repeat(64),
            newPassword: 'Invalid-Test-No-CSRF-Passphrase!',
          }),
        });
        return response.status;
      });
      expect(missingCsrf).toBe(400);
      await staff.locator('input[formControlName="newPassword"]').fill(newPassword);
      await staff.locator('input[formControlName="confirm"]').fill(newPassword);
      await staff.getByRole('button', { name: /ثبت رمز جدید و ورود مجدد/ })
        .click({ noWaitAfter: true });
      await expect(staff.getByRole('status'))
        .toContainText('رمز دائمی ثبت شد', { timeout: 30_000 });
      await expect(staff.getByRole('heading', { name: 'ورود به حساب کاربری' }))
        .toBeVisible();
      expect((await staffContext.cookies(tenantHost))
        .some(cookie => cookie.name === '__Host-Foundation.Bff')).toBe(false);
      expect((await staffContext.cookies(identityHost))
        .some(cookie => cookie.name === '__Host-Foundation.Identity')).toBe(false);

      // Old one-time password is now consumed, not reusable.
      const replay = await staffContext.request.post(
        tenantHost + '/api/public/tenant-first-login/start',
        { data: { tenantSlug: 'e2e-http-ci', userName: username,
                  temporaryPassword: initialPassword } },
      );
      expect(replay.status()).toBe(401);

      // Fresh CAPTCHA + PERMANENT password is required for real OIDC.
      const retryFields = staff.locator('app-custom-input input');
      await expect(retryFields).toHaveCount(2);
      await retryFields.nth(0).fill(username);
      await retryFields.nth(1).fill(newPassword);
      await staff.locator('#captcha-code').fill('ABCDE');
      await staff.locator('form button[type="submit"]').click({ noWaitAfter: true });
      await expect.poll(() => {
        try { const u = new URL(staff.url()); return u.host + u.pathname; }
        catch { return 'pending'; }
      }, { timeout: 45_000 }).toBe('e2e-http-ci.shinera.localhost:8443/dashboard');

      const session = await api(staff, 'GET', '/bff/session');
      expect(session.status).toBe(200);
      expect(session.data.authenticated).toBe(true);
      expect((await staffContext.cookies(tenantHost)).some(cookie =>
        cookie.name === '__Host-Foundation.Bff' &&
        cookie.httpOnly && cookie.secure &&
        cookie.domain === 'e2e-http-ci.shinera.localhost')).toBe(true);
      expect((await api(staff, 'GET', tenantApi + '/members')).status).toBe(403);
      const activated = await api(owner, 'GET', tenantApi + '/members');
      expect(activated.status).toBe(200);
      const newMember = activated.data.items.find((m: any) => m.id === result.member.id);
      expect(newMember?.email).toBeNull();
      expect(newMember?.isActive).toBe(true);

      // Grant member read through audited Owner writes, then confirm live UI/API.
      const createdRole = await api(owner, 'POST', tenantApi + '/roles', {
        name: 'E2E First Login ' + suffix, description: 'Disposable browser scenario',
      }, true);
      expect(createdRole.status).toBe(200);
      const roleId = createdRole.data.id as string;
      const matrix = await api(owner, 'GET',
        tenantApi + '/roles/' + roleId + '/permissions');
      expect(matrix.status).toBe(200);
      expect((await api(owner, 'PUT', tenantApi + '/roles/' + roleId + '/permissions', {
        expectedVersion: matrix.data.version, codes: ['TENANT.MEMBERS.READ'],
      }, true)).status).toBe(200);
      const staffRoles = await api(owner, 'GET',
        tenantApi + '/members/' + result.member.id + '/roles');
      expect(staffRoles.status).toBe(200);
      expect((await api(owner, 'PUT',
        tenantApi + '/members/' + result.member.id + '/roles', {
          expectedVersion: staffRoles.data.version, roleIds: [roleId],
        }, true)).status).toBe(200);

      // Session fingerprint changes must invalidate the OLD BFF cookie.
      expect((await api(staff, 'GET', '/bff/session')).status).toBe(401);
      await staffContext.clearCookies();
      await login(staff, username, '/dashboard', newPassword);
      expect((await api(staff, 'GET', tenantApi + '/members')).status).toBe(200);
      const access = await api(staff, 'GET', tenantApi + '/ui-access/me');
      expect(access.status).toBe(200);
      expect(access.data.entries.some((x: any) =>
        x.key === 'TENANT.MEMBERS.READ' && x.allowed === true)).toBe(true);

      const updatedMatrix = await api(owner, 'GET',
        tenantApi + '/roles/' + roleId + '/permissions');
      expect(updatedMatrix.status).toBe(200);
      expect((await api(owner, 'PUT',
        tenantApi + '/roles/' + roleId + '/permissions', {
          expectedVersion: updatedMatrix.data.version, codes: [],
        }, true)).status).toBe(200);
      expect((await api(staff, 'GET', '/bff/session')).status).toBe(401);
      await staffContext.clearCookies();
      await login(staff, username, '/dashboard', newPassword);
      expect((await api(staff, 'GET', '/bff/session')).status).toBe(200);
      expect((await api(staff, 'GET', tenantApi + '/members')).status).toBe(403);
      const revokedUi = await api(staff, 'GET', tenantApi + '/ui-access/me');
      expect(revokedUi.status).toBe(200);
      expect(revokedUi.data.entries.some((x: any) =>
        x.key === 'TENANT.MEMBERS.READ' && x.allowed === true)).toBe(false);
    } finally {
      await staffContext.close();
      await ownerContext.close();
    }
  });
});

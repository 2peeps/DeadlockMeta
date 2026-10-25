// Deadlock Meta community server: counts how often each crosshair is applied and liked, and keeps the crosshairs
// players share. Runs on Cloudflare Workers with a D1 database bound as DB (see README.md).
// Only an anonymous random install id is stored, so one install counts once per crosshair.

const CODE_LEN = 19;
const MAX_SHARES_PER_DAY = 5;
const STATS_CACHE_SECONDS = 300;

const json = (body, status = 200, extra = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', ...extra } });

const validInstall = (s) => typeof s === 'string' && /^[0-9a-f]{32}$/.test(s);
const validId = (s) => typeof s === 'string' && /^(c|u):[a-z0-9-]{1,48}$/.test(s);

// The 19 numbers the app stores (Crosshair.ToLegacyCode); the 4th is true/false. Returns the normalised code or null.
function normaliseCode(code) {
  if (typeof code !== 'string') return null;
  const parts = code.trim().split(/\s+/);
  if (parts.length !== CODE_LEN) return null;
  const out = [];
  for (let i = 0; i < CODE_LEN; i++) {
    if (i === 3) {
      if (parts[i] !== 'true' && parts[i] !== 'false') return null;
      out.push(parts[i]);
      continue;
    }
    const v = Number(parts[i]);
    if (!Number.isFinite(v) || Math.abs(v) > 1000) return null;
    out.push(String(Math.round(v * 10000) / 10000));
  }
  return out.join(' ');
}

function cleanName(name) {
  if (typeof name !== 'string') return null;
  const n = name.replace(/[\u0000-\u001f\u007f]/g, '').replace(/\s+/g, ' ').trim();
  if (n.length < 2 || n.length > 40) return null;
  // No links or contacts in names.
  if (/https?:|www\.|\.(com|ru|net|org|gg|io)\b|@|t\.me|discord/i.test(n)) return null;
  return n;
}

async function sha12(text) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].slice(0, 6).map((b) => b.toString(16).padStart(2, '0')).join('');
}

async function stats(env) {
  const { results } = await env.DB.prepare(
    `SELECT id, SUM(kind = 'apply') AS applies, SUM(kind = 'like') AS likes FROM events GROUP BY id`
  ).all();
  const items = {};
  for (const r of results) items[r.id] = [r.applies || 0, r.likes || 0];
  return { items };
}

async function community(env) {
  const { results } = await env.DB.prepare(
    `SELECT s.id, s.name, s.code, s.created,
            COALESCE(SUM(e.kind = 'apply'), 0) AS applies, COALESCE(SUM(e.kind = 'like'), 0) AS likes
       FROM shared s LEFT JOIN events e ON e.id = s.id
      WHERE s.hidden = 0
      GROUP BY s.id
      ORDER BY s.created DESC
      LIMIT 500`
  ).all();
  return { items: results };
}

// GET answers are cached at the edge for a few minutes: every app asks hourly and the numbers need not be exact.
async function cached(request, ctx, make) {
  const cache = caches.default;
  const hit = await cache.match(request);
  if (hit) return hit;
  const resp = json(await make(), 200, { 'cache-control': `public, max-age=${STATS_CACHE_SECONDS}` });
  ctx.waitUntil(cache.put(request, resp.clone()));
  return resp;
}

async function event(env, body) {
  const { install, id, kind } = body;
  if (!validInstall(install) || !validId(id) || !['apply', 'like', 'unlike'].includes(kind)) return json({ error: 'bad request' }, 400);
  if (kind === 'unlike') {
    await env.DB.prepare(`DELETE FROM events WHERE id = ? AND install = ? AND kind = 'like'`).bind(id, install).run();
  } else {
    await env.DB.prepare(`INSERT OR IGNORE INTO events (id, install, kind, at) VALUES (?, ?, ?, ?)`)
      .bind(id, install, kind, Math.floor(Date.now() / 1000)).run();
  }
  return json({ ok: true });
}

async function submit(env, body) {
  const install = body.install;
  const name = cleanName(body.name);
  const code = normaliseCode(body.code);
  if (!validInstall(install)) return json({ error: 'Неверный запрос.' }, 400);
  if (!name) return json({ error: 'Название: от 2 до 40 символов, без ссылок.' }, 400);
  if (!code) return json({ error: 'Прицел не похож на настоящий.' }, 400);

  const existing = await env.DB.prepare(`SELECT id FROM shared WHERE code = ?`).bind(code).first();
  if (existing) return json({ id: existing.id, existed: true });

  const now = Math.floor(Date.now() / 1000);
  const recent = await env.DB.prepare(`SELECT COUNT(*) AS n FROM shared WHERE install = ? AND created > ?`)
    .bind(install, now - 86400).first();
  if (recent && recent.n >= MAX_SHARES_PER_DAY) return json({ error: 'Можно делиться не больше 5 прицелов в день.' }, 429);

  const id = 'u:' + (await sha12(code));
  await env.DB.prepare(`INSERT OR IGNORE INTO shared (id, name, code, install, created) VALUES (?, ?, ?, ?, ?)`)
    .bind(id, name, code, install, now).run();
  return json({ id });
}

// Hides a shared crosshair (rude name and the like). Needs the ADMIN_TOKEN secret.
async function hide(env, body) {
  if (!env.ADMIN_TOKEN || body.token !== env.ADMIN_TOKEN) return json({ error: 'forbidden' }, 403);
  if (!validId(body.id)) return json({ error: 'bad id' }, 400);
  await env.DB.prepare(`UPDATE shared SET hidden = ? WHERE id = ?`).bind(body.show ? 0 : 1, body.id).run();
  return json({ ok: true });
}

export default {
  async fetch(request, env, ctx) {
    const { pathname } = new URL(request.url);
    try {
      if (request.method === 'GET' && pathname === '/v1/stats') return cached(request, ctx, () => stats(env));
      if (request.method === 'GET' && pathname === '/v1/community') return cached(request, ctx, () => community(env));
      if (request.method === 'POST') {
        if ((request.headers.get('content-length') | 0) > 4096) return json({ error: 'too large' }, 413);
        const body = await request.json().catch(() => null);
        if (!body || typeof body !== 'object') return json({ error: 'bad json' }, 400);
        if (pathname === '/v1/event') return event(env, body);
        if (pathname === '/v1/submit') return submit(env, body);
        if (pathname === '/v1/hide') return hide(env, body);
      }
      if (pathname === '/') return json({ service: 'deadlockmeta-crosshairs', ok: true });
      return json({ error: 'not found' }, 404);
    } catch (e) {
      return json({ error: 'server error' }, 500);
    }
  },
};

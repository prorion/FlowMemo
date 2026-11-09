// FlowMemo 동기화 서버.
//
// 보안:
// - 모든 API는 기기 토큰이 필요하다. 토큰은 SERVER_SECRET으로 서명되어 있어 위조 토큰은 DB를 읽기 전에 거부한다.
// - 등록 코드는 첫 PC가 쓰면 무효가 되고, 다른 PC 연결 비밀값은 10분 동안 한 번만 쓸 수 있다.
// - 메모 내용(blob)은 PC에서 AES-256-GCM으로 암호화한 값이다. 서버에는 키가 없다.
// - 무료 플랜의 CPU 제한(요청당 10ms) 때문에 느린 비밀번호 해시 대신 256비트 무작위 값을 쓴다.

const PAIRING_LIFETIME_MS = 10 * 60 * 1000;
const PRESENCE_LIFETIME_MS = 45 * 1000;
const MAX_BLOB_LENGTH = 1_500_000; // D1의 행 크기 제한(2MB) 안쪽
const CHANGES_PAGE_SIZE = 100;
const ID_PATTERN = /^[0-9a-f]{32}$/;
const HASH_PATTERN = /^[0-9a-f]{64}$/;

export default {
  async fetch(request, env) {
    try {
      return await route(request, env);
    } catch (error) {
      if (error instanceof HttpError) return json({ error: error.code }, error.status);
      console.error(error);
      return json({ error: "internal" }, 500);
    }
  },
};

class HttpError extends Error {
  constructor(status, code) {
    super(code);
    this.status = status;
    this.code = code;
  }
}

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
  });
}

async function route(request, env) {
  const url = new URL(request.url);
  const path = url.pathname;
  const method = request.method;

  if (method === "GET" && path === "/") return json({ service: "flowmemo-sync" });

  if (method === "POST" && path === "/v1/setup") return setup(request, env);
  if (method === "POST" && path === "/v1/devices/redeem") return redeem(request, env);

  const device = await authenticate(request, env);

  if (method === "GET" && path === "/v1/changes") return getChanges(url, env, device);
  if (method === "POST" && path === "/v1/presence") return postPresence(request, env, device);
  if (method === "POST" && path === "/v1/pairings") return createPairing(request, env);
  if (method === "GET" && path === "/v1/devices") return listDevices(env, device);

  const noteMatch = path.match(/^\/v1\/notes\/([0-9a-f]{32})$/);
  if (noteMatch && method === "PUT") return putNote(noteMatch[1], request, env, device);
  if (noteMatch && method === "DELETE") return deleteNote(noteMatch[1], url, env, device);

  const deviceMatch = path.match(/^\/v1\/devices\/([0-9a-f]{32})$/);
  if (deviceMatch && method === "DELETE") return revokeDevice(deviceMatch[1], env);

  throw new HttpError(404, "not_found");
}

// ---------------------------------------------------------------- 인증

const encoder = new TextEncoder();

function base64UrlEncode(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

function base64UrlDecode(text) {
  const normal = text.replaceAll("-", "+").replaceAll("_", "/");
  const binary = atob(normal + "=".repeat((4 - (normal.length % 4)) % 4));
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

function hmacKey(env, usage) {
  if (!env.SERVER_SECRET) throw new HttpError(500, "server_not_configured");
  return crypto.subtle.importKey("raw", encoder.encode(env.SERVER_SECRET), { name: "HMAC", hash: "SHA-256" }, false, [usage]);
}

async function issueToken(env, deviceId) {
  const key = await hmacKey(env, "sign");
  const signature = await crypto.subtle.sign("HMAC", key, encoder.encode(`device:${deviceId}`));
  return `${deviceId}.${base64UrlEncode(new Uint8Array(signature))}`;
}

async function authenticate(request, env) {
  const header = request.headers.get("authorization") ?? "";
  const match = header.match(/^Bearer ([0-9a-f]{32})\.([A-Za-z0-9_-]{43})$/);
  if (!match) throw new HttpError(401, "unauthorized");

  const [, deviceId, signature] = match;
  const key = await hmacKey(env, "verify");
  const valid = await crypto.subtle.verify("HMAC", key, base64UrlDecode(signature), encoder.encode(`device:${deviceId}`));
  if (!valid) throw new HttpError(401, "unauthorized");

  // 서명이 맞을 때만 DB를 읽는다. 위조 요청이 일일 D1 한도를 쓰지 못하게 한다.
  const device = await env.DB.prepare("SELECT id, name, revoked_at FROM devices WHERE id = ?1").bind(deviceId).first();
  if (!device || device.revoked_at) throw new HttpError(401, "unauthorized");
  return device;
}

async function sha256Hex(text) {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(text)));
  return [...digest].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function newId() {
  return [...crypto.getRandomValues(new Uint8Array(16))].map((b) => b.toString(16).padStart(2, "0")).join("");
}

async function readJson(request) {
  try {
    const body = await request.json();
    if (body && typeof body === "object") return body;
  } catch {
    // 아래에서 400으로 처리
  }
  throw new HttpError(400, "bad_request");
}

function cleanDeviceName(value) {
  const name = typeof value === "string" ? value.trim().slice(0, 64) : "";
  if (!name) throw new HttpError(400, "bad_request");
  return name;
}

function secretsEqual(a, b) {
  const left = encoder.encode(a);
  const right = encoder.encode(b);
  return left.byteLength === right.byteLength && crypto.subtle.timingSafeEqual(left, right);
}

function deviceInsert(env, deviceId, name) {
  return env.DB.prepare("INSERT INTO devices (id, name, created_at) VALUES (?1, ?2, ?3)").bind(deviceId, name, new Date().toISOString());
}

// ---------------------------------------------------------------- 기기 등록

/** 첫 PC 등록. 등록 코드는 한 번만 쓸 수 있다. */
async function setup(request, env) {
  const body = await readJson(request);
  const name = cleanDeviceName(body.deviceName);
  if (typeof body.code !== "string" || typeof body.recoveryHash !== "string" || !HASH_PATTERN.test(body.recoveryHash))
    throw new HttpError(400, "bad_request");
  if (!env.SETUP_CODE || !secretsEqual(await sha256Hex(body.code), await sha256Hex(env.SETUP_CODE)))
    throw new HttpError(403, "invalid_code");

  const deviceId = newId();
  try {
    // setup_claimed가 이미 있으면 기본 키 충돌로 묶음 전체가 취소된다.
    await env.DB.batch([
      env.DB.prepare("INSERT INTO meta (key, value) VALUES ('setup_claimed', '1')"),
      env.DB.prepare("INSERT INTO meta (key, value) VALUES ('recovery_hash', ?1)").bind(body.recoveryHash),
      deviceInsert(env, deviceId, name),
    ]);
  } catch {
    throw new HttpError(403, "invalid_code");
  }

  return json({ deviceId, token: await issueToken(env, deviceId) });
}

/** 다른 PC 연결 코드나 복구 코드로 기기를 등록한다. */
async function redeem(request, env) {
  const body = await readJson(request);
  const name = cleanDeviceName(body.deviceName);
  if (typeof body.secret !== "string" || body.secret.length < 32 || body.secret.length > 128)
    throw new HttpError(400, "bad_request");

  const hash = await sha256Hex(body.secret);
  // DELETE ... RETURNING으로 동시에 두 번 써도 한 번만 성공하게 한다.
  const pairing = await env.DB.prepare("DELETE FROM pairings WHERE secret_hash = ?1 AND expires_at > ?2 RETURNING secret_hash")
    .bind(hash, Date.now())
    .first();

  if (!pairing) {
    const recovery = await env.DB.prepare("SELECT value FROM meta WHERE key = 'recovery_hash'").first();
    if (!recovery || !secretsEqual(hash, recovery.value)) throw new HttpError(403, "invalid_code");
  }

  const deviceId = newId();
  await deviceInsert(env, deviceId, name).run();
  return json({ deviceId, token: await issueToken(env, deviceId) });
}

async function createPairing(request, env) {
  const body = await readJson(request);
  if (typeof body.secretHash !== "string" || !HASH_PATTERN.test(body.secretHash)) throw new HttpError(400, "bad_request");

  const now = Date.now();
  await env.DB.batch([
    env.DB.prepare("DELETE FROM pairings WHERE expires_at <= ?1").bind(now),
    env.DB.prepare("INSERT INTO pairings (secret_hash, expires_at) VALUES (?1, ?2)").bind(body.secretHash, now + PAIRING_LIFETIME_MS),
  ]);
  return json({ expiresAt: now + PAIRING_LIFETIME_MS });
}

async function listDevices(env, device) {
  const { results } = await env.DB.prepare("SELECT id, name, created_at FROM devices WHERE revoked_at IS NULL ORDER BY created_at").all();
  return json({ currentDeviceId: device.id, devices: results.map((d) => ({ id: d.id, name: d.name, createdAt: d.created_at })) });
}

async function revokeDevice(deviceId, env) {
  await env.DB.prepare("UPDATE devices SET revoked_at = ?1 WHERE id = ?2 AND revoked_at IS NULL").bind(new Date().toISOString(), deviceId).run();
  return json({ ok: true });
}

// ---------------------------------------------------------------- 메모

function noteResponse(row) {
  return {
    id: row.id,
    revision: row.revision,
    blob: row.blob,
    deleted: row.deleted === 1,
    deviceId: row.device_id,
    deviceName: row.device_name,
    updatedAt: row.updated_at,
  };
}

async function getChanges(url, env, device) {
  const since = Number.parseInt(url.searchParams.get("since") ?? "0", 10);
  if (!Number.isSafeInteger(since) || since < 0) throw new HttpError(400, "bad_request");

  const now = Date.now();
  const [notes, presence] = await env.DB.batch([
    env.DB.prepare(`SELECT * FROM notes WHERE revision > ?1 ORDER BY revision LIMIT ${CHANGES_PAGE_SIZE}`).bind(since),
    env.DB.prepare("SELECT note_id, device_name FROM presence WHERE until > ?1 AND device_id != ?2").bind(now, device.id),
  ]);

  return json({
    changes: notes.results.map(noteResponse),
    more: notes.results.length === CHANGES_PAGE_SIZE,
    presence: presence.results.map((p) => ({ id: p.note_id, deviceName: p.device_name })),
  });
}

/**
 * 메모를 저장한다. baseRevision이 서버의 현재 값과 다르면 다른 PC가 먼저 고친 것이므로
 * 덮어쓰지 않고 409와 서버의 현재 내용을 돌려준다 (PC에서 사용자가 결정한다).
 */
async function writeNote(id, baseRevision, blob, deleted, env, device) {
  const now = new Date().toISOString();
  const [, upsert] = await env.DB.batch([
    env.DB.prepare("UPDATE meta SET value = CAST(value AS INTEGER) + 1 WHERE key = 'seq'"),
    env.DB.prepare(`
      INSERT INTO notes (id, revision, blob, deleted, device_id, device_name, updated_at)
      SELECT ?1, CAST(value AS INTEGER), ?2, ?3, ?4, ?5, ?6 FROM meta WHERE key = 'seq'
      ON CONFLICT (id) DO UPDATE SET
        revision = excluded.revision, blob = excluded.blob, deleted = excluded.deleted,
        device_id = excluded.device_id, device_name = excluded.device_name, updated_at = excluded.updated_at
      WHERE notes.revision = ?7
      RETURNING revision
    `).bind(id, blob, deleted ? 1 : 0, device.id, device.name, now, baseRevision),
  ]);

  if (upsert.results.length === 0) {
    const current = await env.DB.prepare("SELECT * FROM notes WHERE id = ?1").bind(id).first();
    return json({ error: "conflict", current: current ? noteResponse(current) : null }, 409);
  }

  // 저장이 끝났으면 '편집 중' 표시를 바로 지운다.
  await env.DB.prepare("DELETE FROM presence WHERE note_id = ?1 AND device_id = ?2").bind(id, device.id).run();
  return json({ revision: upsert.results[0].revision });
}

function parseRevision(value) {
  if (!Number.isSafeInteger(value) || value < 0) throw new HttpError(400, "bad_request");
  return value;
}

async function putNote(id, request, env, device) {
  const body = await readJson(request);
  if (typeof body.blob !== "string" || body.blob.length === 0) throw new HttpError(400, "bad_request");
  if (body.blob.length > MAX_BLOB_LENGTH) throw new HttpError(413, "too_large");
  return writeNote(id, parseRevision(body.baseRevision), body.blob, false, env, device);
}

async function deleteNote(id, url, env, device) {
  const baseRevision = Number.parseInt(url.searchParams.get("baseRevision") ?? "", 10);
  return writeNote(id, parseRevision(baseRevision), "-", true, env, device);
}

async function postPresence(request, env, device) {
  const body = await readJson(request);
  if (typeof body.id !== "string" || !ID_PATTERN.test(body.id)) throw new HttpError(400, "bad_request");

  const now = Date.now();
  await env.DB.batch([
    env.DB.prepare("DELETE FROM presence WHERE until <= ?1").bind(now),
    env.DB.prepare(`
      INSERT INTO presence (note_id, device_id, device_name, until) VALUES (?1, ?2, ?3, ?4)
      ON CONFLICT (note_id, device_id) DO UPDATE SET until = excluded.until
    `).bind(body.id, device.id, device.name, now + PRESENCE_LIFETIME_MS),
  ]);
  return json({ ok: true });
}

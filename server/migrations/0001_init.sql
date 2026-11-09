-- FlowMemo 동기화 서버 스키마. 메모 내용(blob)은 PC에서 암호화한 값이라 서버는 읽을 수 없다.

CREATE TABLE meta (
    key   TEXT PRIMARY KEY NOT NULL,
    value TEXT NOT NULL
);
-- seq: 모든 변경에 붙는 전역 순번. PC는 "마지막으로 본 순번 이후"만 받아 간다.
INSERT INTO meta (key, value) VALUES ('seq', '0');

CREATE TABLE devices (
    id         TEXT PRIMARY KEY NOT NULL,
    name       TEXT NOT NULL,
    created_at TEXT NOT NULL,
    revoked_at TEXT
);

CREATE TABLE notes (
    id          TEXT PRIMARY KEY NOT NULL,
    revision    INTEGER NOT NULL,
    blob        TEXT NOT NULL,
    deleted     INTEGER NOT NULL DEFAULT 0,
    device_id   TEXT NOT NULL,
    device_name TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);
CREATE INDEX notes_revision ON notes (revision);

-- 다른 PC 연결 코드. 비밀값은 해시만 저장하고, 한 번 쓰면 지운다.
CREATE TABLE pairings (
    secret_hash TEXT PRIMARY KEY NOT NULL,
    expires_at  INTEGER NOT NULL
);

-- 지금 어느 PC에서 편집 중인지 (충돌 예방용 안내). 잠시 뒤 자동으로 만료된다.
CREATE TABLE presence (
    note_id     TEXT NOT NULL,
    device_id   TEXT NOT NULL,
    device_name TEXT NOT NULL,
    until       INTEGER NOT NULL,
    PRIMARY KEY (note_id, device_id)
);

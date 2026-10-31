#!/usr/bin/env python3
"""Seed and verify the throwaway database used by scripts/account-delete-ui-check.ps1.

The sandbox holds one account that must survive (jimmy) and two junk accounts that the UI run is
supposed to delete, so the verification step can prove the cascade reached every owned table.
"""

from __future__ import annotations

import argparse
import datetime as dt
import os
import pathlib
import sqlite3
import sys
import uuid

SURVIVOR = "jimmy"
JUNK_WITH_DATA = "Junk One"
JUNK_EMPTY = "Junk Two"
# A placeholder number: the sandbox account is never connected, so no real number belongs here.
SANDBOX_PHONE = "+66800000001"

TABLES = [
    "contacts", "telegram_groups", "group_members", "jobs", "job_items", "consent_requests",
    "invite_links", "join_requests", "activity_logs", "account_secrets", "account_sessions",
    "campaigns", "campaign_recipients", "message_templates", "media_files", "template_media",
]


def open_target(path: str) -> pathlib.Path:
    target = pathlib.Path(path).resolve()
    temp = pathlib.Path(os.environ["TEMP"]).resolve()
    if not str(target).lower().startswith(str(temp / "tcs-").lower()):
        raise SystemExit(f"refusing to touch {target}; sandbox copies live under TEMP and start with tcs-")
    if not target.exists():
        raise SystemExit(f"sandbox database not found: {target} (the app creates it on first launch)")
    return target


def now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat()


def seed(connection: sqlite3.Connection) -> None:
    connection.execute("DELETE FROM accounts WHERE display_name IN (?, ?, ?)", (SURVIVOR, JUNK_WITH_DATA, JUNK_EMPTY))
    for table in TABLES:
        connection.execute(f"DELETE FROM {table} WHERE account_id NOT IN (SELECT id FROM accounts)")

    def account(name: str) -> int:
        row = connection.execute("SELECT id FROM accounts WHERE display_name = ?", (name,)).fetchone()
        if row:
            return row[0]
        cursor = connection.execute(
            "INSERT INTO accounts(display_name, phone_number, status, created_at) VALUES(?, ?, 'PendingAuthorization', ?)",
            (name, SANDBOX_PHONE, now()),
        )
        return cursor.lastrowid

    survivor, junk, empty = account(SURVIVOR), account(JUNK_WITH_DATA), account(JUNK_EMPTY)
    stamp = now()

    connection.executemany(
        "INSERT OR REPLACE INTO contacts(account_id, telegram_user_id, first_name, has_consent, is_opted_out, updated_at)"
        " VALUES(?, ?, ?, 1, 0, ?)",
        [(survivor, 900000001, "Real Person", stamp), (survivor, 900000002, "Second Person", stamp),
         (junk, 800000001, "Junk Contact", stamp)],
    )
    connection.executemany(
        "INSERT OR REPLACE INTO jobs(id, account_id, target_type, target_count, status, retry_count, created_at, idempotency_key)"
        " VALUES(?, ?, 'Campaign:consented', 1, 'Completed', 0, ?, ?)",
        [(str(uuid.uuid5(uuid.NAMESPACE_URL, "tcs-account-delete-survivor")), survivor, stamp, "account-delete-survivor"),
         (str(uuid.uuid5(uuid.NAMESPACE_URL, "tcs-account-delete-junk")), junk, stamp, "account-delete-junk")],
    )
    connection.execute(
        "INSERT OR REPLACE INTO job_items(job_id, account_id, target_id, status, retry_count, idempotency_key, updated_at)"
        " SELECT id, ?, 900000001, 'Completed', 0, 'account-delete-item-survivor', ? FROM jobs WHERE idempotency_key = 'account-delete-survivor'",
        (survivor, stamp),
    )
    connection.execute(
        "INSERT OR REPLACE INTO job_items(job_id, account_id, target_id, status, retry_count, idempotency_key, updated_at)"
        " SELECT id, ?, 800000001, 'Completed', 0, 'account-delete-item-junk', ? FROM jobs WHERE idempotency_key = 'account-delete-junk'",
        (junk, stamp),
    )
    connection.execute(
        "INSERT INTO consent_requests(account_id, telegram_user_id, kind, occurred_at, source) VALUES(?, ?, 'Granted', ?, 'operator-ui')",
        (junk, 800000001, stamp),
    )
    connection.execute(
        "INSERT INTO account_secrets(account_id, secret_name, protected_value, updated_at) VALUES(?, 'tdlib.api_id', X'010203', ?)",
        (junk, stamp),
    )
    connection.execute(
        "INSERT OR REPLACE INTO account_sessions(account_id, session_directory, authorization_state, is_authorized, updated_at)"
        " VALUES(?, 'sessions/junk', 'Ready', 0, ?)",
        (junk, stamp),
    )
    connection.commit()
    print(f"ACCOUNTS survivor={survivor} junk={junk} empty={empty}")
    counts = {table: connection.execute(f"SELECT COUNT(*) FROM {table} WHERE account_id = ?", (junk,)).fetchone()[0] for table in TABLES}
    print("JUNK_ROWS", {k: v for k, v in counts.items() if v})


def verify(connection: sqlite3.Connection) -> int:
    names = [row[0] for row in connection.execute("SELECT display_name FROM accounts ORDER BY id")]
    problems = []
    if JUNK_WITH_DATA in names:
        problems.append(f"{JUNK_WITH_DATA} still exists")
    if JUNK_EMPTY in names:
        problems.append(f"{JUNK_EMPTY} still exists")
    if SURVIVOR not in names:
        problems.append(f"{SURVIVOR} was deleted, which must never happen")

    survivor = connection.execute("SELECT id FROM accounts WHERE display_name = ?", (SURVIVOR,)).fetchone()
    if survivor:
        contacts = connection.execute("SELECT COUNT(*) FROM contacts WHERE account_id = ?", (survivor[0],)).fetchone()[0]
        jobs = connection.execute("SELECT COUNT(*) FROM jobs WHERE account_id = ?", (survivor[0],)).fetchone()[0]
        if contacts != 2:
            problems.append(f"survivor contacts={contacts}, expected 2")
        if jobs != 1:
            problems.append(f"survivor jobs={jobs}, expected 1")

    orphans = 0
    for table in TABLES:
        orphans += connection.execute(
            f"SELECT COUNT(*) FROM {table} WHERE account_id NOT IN (SELECT id FROM accounts)"
        ).fetchone()[0]
    if orphans:
        problems.append(f"{orphans} orphan rows left behind")

    print("ACCOUNTS_AFTER", names)
    print("ORPHAN_ROWS", orphans)
    for problem in problems:
        print("FAIL", problem)
    return 1 if problems else 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", required=True)
    parser.add_argument("--mode", choices=["seed", "verify"], required=True)
    args = parser.parse_args()
    target = open_target(args.target)

    connection = sqlite3.connect(target)
    try:
        missing = [t for t in ["accounts", *TABLES] if not connection.execute(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = ?", (t,)).fetchone()[0]]
        if missing:
            raise SystemExit(f"sandbox is missing tables {missing}; launch the app against it once so the schema can migrate")
        if args.mode == "seed":
            seed(connection)
            return 0
        return verify(connection)
    finally:
        connection.close()


if __name__ == "__main__":
    sys.exit(main())

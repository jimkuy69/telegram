"""Builds a throwaway sandbox database for the result-report UI check.

The live database is only ever read here; every row this script writes lands in a copy under TEMP
whose idempotency keys start with 'sandbox-report-' so the synthetic activity can be identified and
removed without touching real data.
"""

from __future__ import annotations

import argparse
import datetime as dt
import os
import pathlib
import shutil
import sqlite3
import sys
import uuid

ACCOUNT_NAME = "Sandbox Report"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", help="optional live database to copy (read only); omit to seed an existing sandbox file")
    parser.add_argument("--target", required=True, help="sandbox copy path, must be under TEMP and named tcs-*")
    args = parser.parse_args()

    target = pathlib.Path(args.target).resolve()
    temp = pathlib.Path(os.environ["TEMP"]).resolve()
    if not str(target).lower().startswith(str(temp / "tcs-").lower()):
        raise SystemExit(f"refusing to write {target}; sandbox copies live under TEMP and start with tcs-")

    if args.source:
        source = pathlib.Path(args.source).resolve()
        if not source.exists():
            raise SystemExit(f"live database not found: {source}")
        shutil.copy2(source, target)
    if not target.exists():
        raise SystemExit(f"sandbox database not found: {target} (start the app once with TELEGRAM_CONTROL_STUDIO_DB to create it)")
    for suffix in ("-wal", "-shm"):
        if pathlib.Path(str(target) + suffix).exists():
            pathlib.Path(str(target) + suffix).unlink()

    connection = sqlite3.connect(target)
    try:
        tables = {r[0] for r in connection.execute("SELECT name FROM sqlite_master WHERE type = 'table'")}
        for required in ("accounts", "jobs", "job_items", "consent_requests"):
            if required not in tables:
                raise SystemExit(f"sandbox database is missing '{required}'; launch the app against it once so the schema can migrate")

        row = connection.execute("SELECT id FROM accounts WHERE display_name = ?", (ACCOUNT_NAME,)).fetchone()
        if row is None:
            created = connection.execute(
                "INSERT INTO accounts(display_name, phone_number, status, created_at) VALUES(?, ?, 'Ready', ?)",
                (ACCOUNT_NAME, "+66000000099", dt.datetime.now(dt.timezone.utc).isoformat()),
            )
            account_id = created.lastrowid
        else:
            account_id = row[0]

        columns = {r[1] for r in connection.execute("PRAGMA table_info(job_items)")}
        added = []
        if "sent_at" not in columns:
            connection.execute("ALTER TABLE job_items ADD COLUMN sent_at TEXT")
            added.append("job_items.sent_at")

        connection.execute("DELETE FROM job_items WHERE idempotency_key LIKE 'sandbox-report-%'")
        connection.execute("DELETE FROM jobs WHERE idempotency_key LIKE 'sandbox-report-%'")
        connection.execute("DELETE FROM consent_requests WHERE source = 'operator-ui' AND matched_keyword = 'sandbox-report'")

        now = dt.datetime.now(dt.timezone.utc)
        iso = lambda delta: (now + delta).isoformat()

        campaign_job = str(uuid.uuid4())
        consent_job = str(uuid.uuid4())
        connection.executemany(
            "INSERT INTO jobs(id, account_id, target_type, target_count, status, retry_count, created_at, "
            "started_at, completed_at, idempotency_key, message_text) VALUES(?, ?, ?, ?, ?, 0, ?, ?, ?, ?, ?)",
            [
                (campaign_job, account_id, "Campaign:consented", 6, "PartiallyCompleted",
                 iso(dt.timedelta(days=-3)), iso(dt.timedelta(days=-3)), iso(dt.timedelta(days=-1)),
                 "sandbox-report-campaign", "sandbox activity for the report UI check"),
                (consent_job, account_id, "ConsentRequest:1", 2, "PartiallyCompleted",
                 iso(dt.timedelta(days=-2)), iso(dt.timedelta(days=-2)), iso(dt.timedelta(days=-1)),
                 "sandbox-report-consent", "sandbox opt-in request for the report UI check"),
            ],
        )

        campaign_items = [
            (700000001, "Completed", iso(dt.timedelta(days=-2, hours=-3)), None),
            (700000002, "Completed", iso(dt.timedelta(days=-1, hours=-5)), None),
            (700000003, "Completed", iso(dt.timedelta(hours=-6)), None),
            (700000004, "Failed", None, "TDLIB_MESSAGE_SEND_FAILED"),
            (700000005, "Skipped", None, "CONSENT_REQUIRED_OR_OPTED_OUT"),
            (700000006, "WaitingForFloodWait", None, "FLOOD_WAIT_3600"),
        ]
        consent_items = [
            (700000011, "Completed", iso(dt.timedelta(days=-1, hours=-2)), None),
            (700000012, "Skipped", None, "CONSENT_REQUEST_DAILY_LIMIT"),
        ]
        inserted_items = 0
        for job_id, rows in ((campaign_job, campaign_items), (consent_job, consent_items)):
            for target_id, status, sent_at, error in rows:
                updated = sent_at or iso(dt.timedelta(hours=-1))
                connection.execute(
                    "INSERT INTO job_items(job_id, account_id, target_id, status, retry_count, idempotency_key, "
                    "last_error, updated_at, sent_at) VALUES(?, ?, ?, ?, 0, ?, ?, ?, ?)",
                    (job_id, account_id, target_id, status, f"sandbox-report-{target_id}", error, updated, sent_at),
                )
                inserted_items += 1

        consent_events = [
            (700000001, "Requested", dt.timedelta(days=-2)),
            (700000002, "Requested", dt.timedelta(days=-1)),
            (700000003, "Requested", dt.timedelta(hours=-6)),
            (700000011, "Requested", dt.timedelta(days=-1, hours=-2)),
            (700000001, "Granted", dt.timedelta(days=-1, hours=-20)),
            (700000002, "Rejected", dt.timedelta(hours=-30)),
            (700000003, "OptedOut", dt.timedelta(hours=-5)),
        ]
        inserted_events = 0
        for user_id, kind, age in consent_events:
            connection.execute(
                "INSERT INTO consent_requests(account_id, telegram_user_id, kind, occurred_at, message_id, chat_id, "
                "template_id, body_sha256, matched_keyword, source) VALUES(?, ?, ?, ?, 700001, 700002, 1, ?, 'sandbox-report', 'operator-ui')",
                (account_id, user_id, kind, iso(age), "0" * 64 if kind == "Requested" else None),
            )
            inserted_events += 1

        connection.commit()
        print(f"SANDBOX={target}")
        print(f"ACCOUNT_ID={account_id}")
        print(f"SCHEMA_ADDED={added}")
        print(f"JOBS_INSERTED=2 ITEMS_INSERTED={inserted_items} CONSENT_EVENTS_INSERTED={inserted_events}")
        print(f"SENT_AT_ROWS={connection.execute('SELECT COUNT(*) FROM job_items WHERE sent_at IS NOT NULL AND idempotency_key LIKE ?', ('sandbox-report-%',)).fetchone()[0]}")
    finally:
        connection.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())

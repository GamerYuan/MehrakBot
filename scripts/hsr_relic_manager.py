#!/usr/bin/env python3
"""
Manage HSR relic set mappings in Postgres (table: HsrRelics).

Schema (table "HsrRelics"):
- SetId   (int, primary key)
- SetName (text, required)

Usage:
    python3 hsr_relic_manager.py add [set id] [set name]
    python3 hsr_relic_manager.py delete [set id]
    python3 hsr_relic_manager.py update [set id] [new set name]

Connection settings:
- Reads ../.env (relative to this script) before CLI parsing.
- Defaults: POSTGRES_HOST=localhost, POSTGRES_PORT=5433, POSTGRES_DB=mehrak_dev,
    POSTGRES_USER=postgres (password from POSTGRES_PASSWORD). You can also
    pass a full connection string with --conn.
"""

import argparse
import os
import sys
from pathlib import Path
from typing import Tuple

import psycopg


def _load_env_from_parent(override: bool = False) -> None:
    """Load environment variables from ../.env relative to this script.

    Only sets variables that are not already defined unless override=True.
    Supports simple KEY=VALUE pairs and optional quotes.
    """
    try:
        env_path = Path(__file__).resolve().parent.parent / ".env"
    except Exception:
        return

    if not env_path.exists():
        return

    try:
        for line in env_path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            # Support leading `export `
            if line.startswith("export "):
                line = line[len("export ") :]
            if "=" not in line:
                continue
            key, value = line.split("=", 1)
            key = key.strip()
            value = value.strip().strip('"').strip("'")
            if override or key not in os.environ:
                os.environ[key] = value
    except Exception:
        # Silently ignore dotenv parse errors to avoid breaking CLI
        pass


def build_conninfo(args: argparse.Namespace) -> Tuple[bool, dict]:
    if args.conn:
        return False, {"conninfo": args.conn}

    env_conn = os.getenv("POSTGRES_CONNECTION_STRING") or os.getenv("DATABASE_URL")
    if env_conn:
        return False, {"conninfo": env_conn}

    host = args.host or os.getenv("POSTGRES_HOST", "localhost")
    port = args.port or int(os.getenv("POSTGRES_PORT", "5433"))
    db_name = args.db or os.getenv("POSTGRES_DB", "mehrak_dev")
    user = args.user or os.getenv("POSTGRES_USER", "postgres")
    password = args.password or os.getenv("POSTGRES_PASSWORD", "")

    return True, {
        "host": host,
        "port": port,
        "dbname": db_name,
        "user": user,
        "password": password,
    }


def parse_args() -> Tuple[str, argparse.Namespace]:
    parser = argparse.ArgumentParser(description="Manage HSR relic set mappings")
    parser.add_argument(
        "command",
        choices=["add", "update", "delete"],
        help="Operation to perform",
    )
    parser.add_argument("set_id", type=int, help="Relic set id (integer)")
    parser.add_argument(
        "set_name",
        nargs="?",
        help="Set name (required for add and update)",
    )
    parser.add_argument(
        "--conn",
        dest="conn",
        help="Full Postgres connection string/URL (overrides other connection options)",
    )
    parser.add_argument(
        "--host",
        dest="host",
        help="Postgres host (default env POSTGRES_HOST or localhost)",
    )
    parser.add_argument(
        "--port",
        dest="port",
        type=int,
        help="Postgres port (default env POSTGRES_PORT or 5433)",
    )
    parser.add_argument(
        "--db", dest="db", help="Database name (default env POSTGRES_DB or mehrak_dev)"
    )
    parser.add_argument(
        "--user", dest="user", help="Database user (default env POSTGRES_USER)"
    )
    parser.add_argument(
        "--password",
        dest="password",
        help="Database password (default env POSTGRES_PASSWORD)",
    )

    args = parser.parse_args()

    if args.command in ("add", "update") and not args.set_name:
        parser.error("set_name is required for add and update commands")

    return args.command, args


def cmd_add(conn: psycopg.Connection, set_id: int, set_name: str) -> int:
    try:
        with conn.cursor() as cur:
            cur.execute(
                'INSERT INTO "HsrRelics" ("SetId", "SetName") VALUES (%s, %s) '
                'ON CONFLICT ("SetId") DO NOTHING RETURNING "SetId";',
                (set_id, set_name),
            )
            row = cur.fetchone()
            if row:
                print(f"Inserted: set_id={set_id}, set_name='{set_name}'")
            else:
                print(f"Exists: set_id={set_id} already present. No changes made.")
            conn.commit()
        return 0
    except Exception as e:
        print(f"Error adding mapping: {e}", file=sys.stderr)
        return 1


def cmd_update(conn: psycopg.Connection, set_id: int, set_name: str) -> int:
    try:
        with conn.cursor() as cur:
            cur.execute(
                'UPDATE "HsrRelics" SET "SetName" = %s WHERE "SetId" = %s;',
                (set_name, set_id),
            )
            if cur.rowcount == 0:
                print(f"Not found: set_id={set_id}. Nothing updated.")
                return 1
            conn.commit()
            print(f"Updated: set_id={set_id}, set_name='{set_name}'")
            return 0
    except Exception as e:
        print(f"Error updating mapping: {e}", file=sys.stderr)
        return 1


def cmd_delete(conn: psycopg.Connection, set_id: int) -> int:
    try:
        with conn.cursor() as cur:
            cur.execute('DELETE FROM "HsrRelics" WHERE "SetId" = %s;', (set_id,))
            if cur.rowcount == 0:
                print(f"Not found: set_id={set_id}. Nothing deleted.")
                return 1
            conn.commit()
            print(f"Deleted: set_id={set_id}")
            return 0
    except Exception as e:
        print(f"Error deleting mapping: {e}", file=sys.stderr)
        return 1


def main() -> int:
    # Load ../.env before reading defaults from environment
    _load_env_from_parent()

    command, args = parse_args()
    use_kwargs, conn_kwargs = build_conninfo(args)
    try:
        with (
            psycopg.connect(**conn_kwargs)
            if use_kwargs
            else psycopg.connect(conn_kwargs["conninfo"])
        ) as conn:
            if command == "add":
                return cmd_add(conn, args.set_id, args.set_name)
            if command == "update":
                return cmd_update(conn, args.set_id, args.set_name)
            if command == "delete":
                return cmd_delete(conn, args.set_id)
    except Exception as e:
        print(f"Failed to connect to Postgres: {e}", file=sys.stderr)
        return 2

    print("Unknown command", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())

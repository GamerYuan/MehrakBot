import argparse
import os
from pathlib import Path
import sys
from typing import Dict, Union

import psycopg

GameValue = int


def _normalize_game(value: str) -> str:
    return value.lower().replace(" ", "").replace("_", "")


GAME_ALIASES: Dict[str, GameValue] = {
    "genshin": 1,
    "gi": 1,
    "hk4e": 1,
    "honkaiimpact3": 4,
    "hi3": 4,
    "bh3": 4,
    "honkaiimpact3rd": 4,
    "honkaistarrail": 2,
    "hsr": 2,
    "hkrpg": 2,
    "zenlesszonezero": 3,
    "zzz": 3,
    "zenless": 3,
    "tearsofthemis": 5,
    "tot": 5,
}


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


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Manage redeem codes in the Postgres Codes table",
        epilog="Defaults use env vars POSTGRES_* (port 5433).",
    )
    parser.add_argument(
        "command", choices=["add", "delete"], help="Operation to perform"
    )
    parser.add_argument("game", help="Game name, e.g. Genshin, HSR, ZZZ")
    parser.add_argument("code", help="Redeem code")
    parser.add_argument(
        "--conn", dest="conn", help="Full Postgres connection string/URL"
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
    return parser.parse_args()


def resolve_game(game_input: str) -> GameValue:
    normalized = _normalize_game(game_input)
    if normalized in GAME_ALIASES:
        return GAME_ALIASES[normalized]
    # Allow exact enum names from the codebase
    name_map = {
        "unsupported": 0,
        "genshin": 1,
        "honkaistarrail": 2,
        "zenlesszonezero": 3,
        "honkaiimpact3": 4,
        "tearsofthemis": 5,
    }
    if normalized in name_map:
        return name_map[normalized]
    raise ValueError(
        f"Unknown game '{game_input}'. Supported: {', '.join(sorted(set(GAME_ALIASES)))}"
    )


def build_conninfo(args: argparse.Namespace) -> Union[str, Dict[str, Union[str, int]]]:
    if args.conn:
        return args.conn

    env_conn = os.getenv("POSTGRES_CONNECTION_STRING") or os.getenv("DATABASE_URL")
    if env_conn:
        return env_conn

    host = args.host or os.getenv("POSTGRES_HOST", "localhost")
    port = args.port or int(os.getenv("POSTGRES_PORT", "5433"))
    db_name = args.db or os.getenv("POSTGRES_DB", "mehrak_dev")
    user = args.user or os.getenv("POSTGRES_USER", "postgres")
    password = args.password or os.getenv("POSTGRES_PASSWORD", "")

    return {
        "host": host,
        "port": port,
        "dbname": db_name,
        "user": user,
        "password": password,
    }


def add_code(conn: psycopg.Connection, game_value: GameValue, code: str) -> None:
    with conn.cursor() as cur:
        cur.execute(
            'INSERT INTO "Codes" ("Game", "Code") VALUES (%s, %s) '
            'ON CONFLICT ("Game", "Code") DO NOTHING RETURNING "Id";',
            (game_value, code),
        )
        row = cur.fetchone()
        if row:
            print(f"Added code '{code}' for game '{game_value}' (Id={row[0]})")
        else:
            print(f"Code '{code}' already exists for game '{game_value}'")


def delete_code(conn: psycopg.Connection, game_value: GameValue, code: str) -> None:
    with conn.cursor() as cur:
        cur.execute(
            'DELETE FROM "Codes" WHERE "Game" = %s AND "Code" = %s;',
            (game_value, code),
        )
        if cur.rowcount and cur.rowcount > 0:
            print(f"Removed code '{code}' for game '{game_value}'")
        else:
            print(f"Code '{code}' not found for game '{game_value}'")


def main() -> int:
    _load_env_from_parent()

    args = parse_args()
    try:
        game_value = resolve_game(args.game)
    except ValueError as exc:  # invalid game name
        print(str(exc), file=sys.stderr)
        return 1

    conninfo = build_conninfo(args)
    code = args.code.upper()

    try:
        with (
            psycopg.connect(**conninfo)
            if isinstance(conninfo, dict)
            else psycopg.connect(conninfo)
        ) as conn:
            if args.command == "add":
                add_code(conn, game_value, code)
            else:
                delete_code(conn, game_value, code)
            conn.commit()
    except Exception as exc:
        print(f"Database error: {exc}", file=sys.stderr)
        return 2

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

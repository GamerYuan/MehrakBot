#!/usr/bin/env python3
"""
Manage HSR relic set mappings in MongoDB.

Schema (collection: hsr_relics):
- set_id   (int)
- set_name (string)

Usage:
  python3 hsr_relic_manager.py add [set id] [set name]
  python3 hsr_relic_manager.py delete [set id]
  python3 hsr_relic_manager.py update [set id] [new set name]

Connection settings:
- Reads ../.env (relative to this script) and loads environment variables
  before parsing CLI options.
- Uses env vars MONGODB_CONNECTION_STRING and MONGODB_DATABASE_NAME
  or pass --conn and --db options to override.
"""

import argparse
import os
import sys
from pathlib import Path
from typing import Tuple

from pymongo import MongoClient, ASCENDING
from pymongo.collection import Collection
from pymongo.errors import DuplicateKeyError, PyMongoError


def _load_env_from_parent(override: bool = False) -> None:
    """Load environment variables from ../.env relative to this script.

    Only sets variables that are not already defined unless override=True.
    Supports simple KEY=VALUE pairs and optional quotes.
    """
    try:
        env_path = (Path(__file__).resolve().parent.parent / ".env")
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
                line = line[len("export "):]
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


def get_collection(conn: str, db_name: str) -> Collection:
    client = MongoClient(conn)
    db = client[db_name]
    coll = db["hsr_relics"]
    # Ensure unique index on set_id
    coll.create_index([("set_id", ASCENDING)], unique=True)
    return coll


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
        default=os.environ.get("MONGODB_CONNECTION_STRING", "mongodb://localhost:27017"),
        help="MongoDB connection string (default from env MONGODB_CONNECTION_STRING or mongodb://localhost:27017)",
    )
    parser.add_argument(
        "--db",
        dest="db",
        default=os.environ.get("MONGODB_DATABASE_NAME", "mehrak"),
        help="MongoDB database name (default from env MONGODB_DATABASE_NAME or 'mehrak')",
    )

    args = parser.parse_args()

    if args.command in ("add", "update") and not args.set_name:
        parser.error("set_name is required for add and update commands")

    return args.command, args


def cmd_add(coll: Collection, set_id: int, set_name: str) -> int:
    try:
        # Only insert if it doesn't exist
        result = coll.update_one(
            {"set_id": set_id},
            {"$setOnInsert": {"set_id": set_id, "set_name": set_name}},
            upsert=True,
        )
        if result.upserted_id is not None:
            print(f"Inserted: set_id={set_id}, set_name='{set_name}'")
            return 0
        else:
            print(f"Exists: set_id={set_id} already present. No changes made.")
            return 0
    except DuplicateKeyError:
        print(f"Exists: set_id={set_id} already present. No changes made.")
        return 0
    except PyMongoError as e:
        print(f"Error adding mapping: {e}", file=sys.stderr)
        return 1


def cmd_update(coll: Collection, set_id: int, set_name: str) -> int:
    try:
        result = coll.update_one({"set_id": set_id}, {"$set": {"set_name": set_name}}, upsert=False)
        if result.matched_count == 0:
            print(f"Not found: set_id={set_id}. Nothing updated.")
            return 1
        print(f"Updated: set_id={set_id}, set_name='{set_name}'")
        return 0
    except PyMongoError as e:
        print(f"Error updating mapping: {e}", file=sys.stderr)
        return 1


def cmd_delete(coll: Collection, set_id: int) -> int:
    try:
        result = coll.delete_one({"set_id": set_id})
        if result.deleted_count == 0:
            print(f"Not found: set_id={set_id}. Nothing deleted.")
            return 1
        print(f"Deleted: set_id={set_id}")
        return 0
    except PyMongoError as e:
        print(f"Error deleting mapping: {e}", file=sys.stderr)
        return 1


def main() -> int:
    # Load ../.env before reading defaults from environment
    _load_env_from_parent()

    command, args = parse_args()
    try:
        coll = get_collection(args.conn, args.db)
    except Exception as e:  # connection errors
        print(f"Failed to connect to MongoDB: {e}", file=sys.stderr)
        return 2

    if command == "add":
        return cmd_add(coll, args.set_id, args.set_name)
    if command == "update":
        return cmd_update(coll, args.set_id, args.set_name)
    if command == "delete":
        return cmd_delete(coll, args.set_id)

    print("Unknown command", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())

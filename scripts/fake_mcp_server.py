#!/usr/bin/env python3
"""
Tiny configurable MCP stdio server for Codex debugging.

Features:
- Logs all inbound frames to stderr (never stdout).
- Responds to `initialize` with a hardcoded result.
- Optional PascalCase keys (JsonRpc/Id/Method/Params/Result) to probe casing quirks.
- Optional duplicate initialize responses (to simulate conflicts).
- Minimal support for tools/list (returns empty list).

Env toggles:
  PASCAL_CASE=1     -> use JsonRpc/Id/Method/Params/Result casing
  DUP_INIT=1        -> send two initialize responses with the same id
"""

import json
import os
import sys
from typing import Any, Dict


def to_pascal_case(enabled: bool) -> Dict[str, str]:
    if enabled:
        return {
            "jsonrpc": "JsonRpc",
            "id": "Id",
            "method": "Method",
            "params": "Params",
            "result": "Result",
            "error": "Error",
        }
    return {
        "jsonrpc": "jsonrpc",
        "id": "id",
        "method": "method",
        "params": "params",
        "result": "result",
        "error": "error",
    }


def build_initialize_response(keys: Dict[str, str], req: Dict[str, Any]) -> Dict[str, Any]:
    req_id_raw = req.get(keys["id"]) if keys["id"] in req else req.get("id")
    force_string_id = os.environ.get("RESPONSE_ID_STRING", "0") == "1"
    if force_string_id and req_id_raw is not None:
        req_id = str(req_id_raw)
    else:
        req_id = req_id_raw if req_id_raw is not None else "0"
    return {
        keys["jsonrpc"]: "2.0",
        keys["id"]: req_id if req_id is not None else "0",
        keys["result"]: {
            "protocolVersion": "2025-06-18",
            "capabilities": {
                "tools": {},
                "resources": {},
                "prompts": {},
            },
            "serverInfo": {
                "name": os.environ.get("SERVER_NAME", "Fake MCP Server"),
                "title": os.environ.get("SERVER_TITLE", "Fake MCP Server"),
                "version": os.environ.get("SERVER_VERSION", "0.0.0-dev"),
            },
            "instructions": os.environ.get(
                "INSTRUCTIONS",
                "Debug stub server for Codex MCP handshake testing.",
            ),
        },
    }


def build_tools_list_response(keys: Dict[str, str], req: Dict[str, Any]) -> Dict[str, Any]:
    req_id_raw = req.get(keys["id"]) if keys["id"] in req else req.get("id")
    force_string_id = os.environ.get("RESPONSE_ID_STRING", "0") == "1"
    req_id = str(req_id_raw) if force_string_id and req_id_raw is not None else req_id_raw
    return {
        keys["jsonrpc"]: "2.0",
        keys["id"]: req_id if req_id is not None else "1",
        keys["result"]: {"tools": []},
    }


def build_error(keys: Dict[str, str], req: Dict[str, Any], code: int, message: str) -> Dict[str, Any]:
    req_id = req.get(keys["id"]) if keys["id"] in req else req.get("id")
    return {
        keys["jsonrpc"]: "2.0",
        keys["id"]: req_id,
        keys["error"]: {"code": code, "message": message},
    }


def log_stderr(message: str) -> None:
    sys.stderr.write(message + "\n")
    sys.stderr.flush()


def log_file_writer(path: str):
    try:
        directory = os.path.dirname(os.path.abspath(path))
        if directory:
            os.makedirs(directory, exist_ok=True)
        fh = open(path, "a", buffering=1, encoding="utf-8")
    except Exception as ex:
        sys.stderr.write(f"[fake-mcp] failed to open log file {path}: {ex}\n")
        sys.stderr.flush()
        return None

    def _write(msg: str) -> None:
        try:
            fh.write(msg + "\n")
            fh.flush()
        except Exception:
            # Swallow logging errors to avoid breaking the server
            pass

    return fh, _write


def main() -> int:
    pascal_case = os.environ.get("PASCAL_CASE", "0") == "1"
    dup_init = os.environ.get("DUP_INIT", "0") == "1"
    log_path = os.environ.get("LOG_PATH", "/tmp/fake_mcp_server.log")
    keys = to_pascal_case(pascal_case)

    log_handle_write = None
    fh_and_writer = log_file_writer(log_path)
    if fh_and_writer:
        fh, log_handle_write = fh_and_writer
    else:
        fh = None

    def log(msg: str) -> None:
        log_stderr(msg)
        if log_handle_write:
            log_handle_write(msg)

    log(f"[fake-mcp] start (pascal_case={pascal_case}, dup_init={dup_init}, log_path={log_path})")

    for line in sys.stdin:
        raw = line.strip()
        if not raw:
            continue
        log(f"[fake-mcp] recv: {raw}")
        try:
            msg = json.loads(raw)
        except json.JSONDecodeError as ex:
            log(f"[fake-mcp] decode error: {ex}")
            continue

        method = msg.get(keys["method"]) or msg.get("method")
        if method == "initialize":
            resp = build_initialize_response(keys, msg)
            out = json.dumps(resp, separators=(",", ":"))
            sys.stdout.write(out + "\n")
            sys.stdout.flush()
            log(f"[fake-mcp] send: {out}")
            if dup_init:
                # Send a second initialize response to simulate a conflict.
                sys.stdout.write(out + "\n")
                sys.stdout.flush()
                log(f"[fake-mcp] send-dup: {out}")
        elif method == "tools/list":
            resp = build_tools_list_response(keys, msg)
            out = json.dumps(resp, separators=(",", ":"))
            sys.stdout.write(out + "\n")
            sys.stdout.flush()
            log(f"[fake-mcp] send: {out}")
        else:
            resp = build_error(keys, msg, -32601, f"Unknown method {method}")
            out = json.dumps(resp, separators=(",", ":"))
            sys.stdout.write(out + "\n")
            sys.stdout.flush()
            log(f"[fake-mcp] send: {out}")

    log("[fake-mcp] stdin closed; exiting")
    if fh:
        try:
            fh.close()
        except Exception:
            pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

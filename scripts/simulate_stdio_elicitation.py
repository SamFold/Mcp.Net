#!/usr/bin/env python3
"""
Drive a full stdio MCP flow against SimpleServer, including an elicitation round-trip.

Flow:
- Start SimpleServer in stdio mode (no auth)
- initialize (elicitation capability advertised)
- tools/call wh40k_inquisitor_name
- When the server sends elicitation/create, reply with an accepted alias
- Print key frames and exit after the tool response arrives
"""

import json
from pathlib import Path
import select
import subprocess
import sys
import time
from typing import Any, Dict, Optional

REPO_ROOT = Path(__file__).resolve().parents[1]
SIMPLE_SERVER_PROJECT = REPO_ROOT / "Mcp.Net.Examples.SimpleServer" / "Mcp.Net.Examples.SimpleServer.csproj"

SERVER_CMD = [
    "dotnet",
    "run",
    "--project",
    str(SIMPLE_SERVER_PROJECT),
    "--",
    "--stdio",
    "--no-auth",
    "--log-level",
    "Debug",
]


def send(proc: subprocess.Popen, obj: Dict[str, Any]) -> None:
    line = json.dumps(obj, separators=(",", ":"))
    assert proc.stdin is not None
    proc.stdin.write(line + "\n")
    proc.stdin.flush()
    print(f"[client -> server] {line}", flush=True)


def read_line(proc: subprocess.Popen, timeout: float = 5.0) -> Optional[str]:
    assert proc.stdout is not None
    rlist, _, _ = select.select([proc.stdout], [], [], timeout)
    if not rlist:
        return None
    return proc.stdout.readline()


def main() -> int:
    print("Starting SimpleServer for stdio elicitation demo…", flush=True)
    proc = subprocess.Popen(
        SERVER_CMD,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )

    tool_call_id = 1
    pending_response_received = False
    final_response: Optional[Dict[str, Any]] = None

    try:
        # initialize with elicitation capability
        send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 0,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2025-06-18",
                    "capabilities": {"elicitation": {}},
                    "clientInfo": {"name": "elicitation-demo", "version": "0.0.1"},
                },
            },
        )

        line = read_line(proc, timeout=5)
        if line:
            print(f"[server -> client] initialize response: {line.strip()}", flush=True)

        send(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})

        # call the Warhammer inquisitor tool (this triggers elicitation)
        send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": tool_call_id,
                "method": "tools/call",
                "params": {
                    "name": "wh40k_inquisitor_name",
                    "arguments": {"includeTitle": True},
                },
            },
        )

        start = time.time()
        timeout = 15.0

        while time.time() - start < timeout:
            line = read_line(proc, timeout=timeout - (time.time() - start))
            if not line:
                continue

            print(f"[server -> client] {line.strip()}", flush=True)
            try:
                payload = json.loads(line)
            except json.JSONDecodeError:
                continue

            if "method" in payload and payload.get("method") == "elicitation/create":
                # Respond to elicitation with an alias
                response = {
                    "jsonrpc": "2.0",
                    "id": payload.get("id"),
                    "result": {
                        "action": "accept",
                        "content": {"alias": "InqCodex"},
                    },
                }
                send(proc, response)
                continue

            if "id" in payload and payload.get("id") == tool_call_id:
                pending_response_received = True
                final_response = payload
                break

        if not pending_response_received:
            print("Timed out waiting for tool response", flush=True)
        else:
            print("Received tool response:")
            print(json.dumps(final_response, indent=2))
            print("Shutting down", flush=True)
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=5)

        stderr_output = proc.stderr.read() if proc.stderr else ""
        if stderr_output:
            print("Stderr:\n" + stderr_output, flush=True)
        print(f"Server exited with {proc.returncode}", flush=True)

    return 0


if __name__ == "__main__":
    sys.exit(main())

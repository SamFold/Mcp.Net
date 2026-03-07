#!/usr/bin/env python3
"""
Minimal Codex-style stdio handshake harness for SimpleServer.

- Sends initialize (id=0) with Codex client info
- Prints the server's stdout frames so you can see exact JSON-RPC responses
- Sends notifications/initialized, then tools/list (id=1) as a sanity check
"""

import json
from pathlib import Path
import subprocess
import sys
import time


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


def send(proc: subprocess.Popen, obj: dict) -> None:
    line = json.dumps(obj, separators=(",", ":"))
    assert proc.stdin is not None
    proc.stdin.write(line + "\n")
    proc.stdin.flush()
    print(f"[client -> server] {line}", flush=True)


def read(proc: subprocess.Popen, label: str) -> None:
    assert proc.stdout is not None
    line = proc.stdout.readline()
    if not line:
        print(f"[server -> client] {label}: <no data>", flush=True)
        return
    print(f"[server -> client] {label}: {line.strip()}", flush=True)

def read_with_timeout(proc: subprocess.Popen, label: str, timeout: float = 1.0) -> None:
    import select

    assert proc.stdout is not None
    rlist, _, _ = select.select([proc.stdout], [], [], timeout)
    if not rlist:
        print(f"[server -> client] {label}: <no response within {timeout}s>", flush=True)
        return
    read(proc, label)


def main() -> int:
    print("Starting SimpleServer…", flush=True)
    proc = subprocess.Popen(
        SERVER_CMD,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )

    try:
        # 1) Send initialize exactly matching Codex logging (field casing + id numeric)
        send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 0,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2025-06-18",
                    "capabilities": {"elicitation": {}},
                    "clientInfo": {
                        "name": "codex-mcp-client",
                        "title": "Codex",
                        "version": "0.63.0",
                    },
                },
            },
        )

        read(proc, "initialize response")

        # 2) Send initialized notification (Codex follows with this)
        send(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})

        # 3) Codex won't send a request here, but we can probe if server emits a response
        #    by reading one more line if present. Notification shouldn't get a response,
        #    so use a short timeout to avoid blocking.
        read_with_timeout(proc, "post-initialized response (if any)", timeout=1.0)

        # 4) Optional sanity check: list tools
        send(proc, {"jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {}})
        read(proc, "tools/list response")

        # Give the server a moment, then shut down
        time.sleep(0.5)
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

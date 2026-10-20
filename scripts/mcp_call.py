"""
Horizun Civil 3D MCP - call the INSTALLED server over stdio, exactly as an MCP client does.

Use it to verify live when the horizun_c3d_* tools are not loaded in your session
(e.g. the session started before the server was registered).

  python scripts/mcp_call.py '[["horizun_c3d_health",{}]]'
  python scripts/mcp_call.py '[["horizun_c3d_query",{"action":"list","type":"surface"}]]' 8000

Argument 1: JSON list of [tool_name, arguments] pairs (sent concurrently -> exercises the queue).
Argument 2 (optional): max characters printed per result (default 4000).
As a module: from mcp_call import run; run([[name, args], ...]) -> list of MCP results.
"""
import json, os, subprocess, sys

def installed_server(root):
    """Honor a verified installation's isolated server, confined to this product root."""
    manifest = os.path.join(root, "manifest.json")
    directory = os.path.join(root, "server")
    if os.path.isfile(manifest):
        with open(manifest, encoding="utf-8-sig") as stream:
            value = json.load(stream).get("server_dir")
        if not isinstance(value, str) or not value or not os.path.isabs(value):
            raise RuntimeError("Installed server_dir must be an absolute path")
        directory = os.path.realpath(value)
        product_root = os.path.realpath(root)
        if os.path.normcase(os.path.commonpath([product_root, directory])) != os.path.normcase(product_root):
            raise RuntimeError("Installed server_dir escapes the Civil3D MCP product root")
    return os.path.join(directory, "horizun-civil3d-mcp.exe")


EXE = installed_server(os.path.join(os.environ.get("LOCALAPPDATA", ""), "Programs", "Horizun", "Civil3D-MCP"))

def run(calls, timeout=900):
    msgs = [{"jsonrpc": "2.0", "id": 0, "method": "initialize",
             "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "mcp_call", "version": "1"}}},
            {"jsonrpc": "2.0", "method": "notifications/initialized"}]
    for i, (name, args) in enumerate(calls, 1):
        msgs.append({"jsonrpc": "2.0", "id": i, "method": "tools/call", "params": {"name": name, "arguments": args}})
    data = ("\n".join(json.dumps(m) for m in msgs) + "\n").encode("utf-8")
    p = subprocess.run([EXE], input=data, capture_output=True, timeout=timeout)
    out = {}
    for line in p.stdout.decode("utf-8").splitlines():
        m = json.loads(line)
        if m.get("id"):
            out[m["id"]] = m.get("result") or {"isError": True, "structuredContent": m.get("error")}
    return [out.get(i) for i in range(1, len(calls) + 1)]

class Session:
    """One live server process for several dependent calls (e.g. horizun_c3d_target, then dry run -> token -> apply),
    exactly like an MCP client session: the instance chosen with horizun_c3d_target sticks for the whole session."""

    def __init__(self):
        self.p = subprocess.Popen([EXE], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        self.n = 0
        self._send({"jsonrpc": "2.0", "id": 0, "method": "initialize",
                    "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "mcp_call", "version": "1"}}})
        self._read(0)
        self._send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def _send(self, m):
        self.p.stdin.write((json.dumps(m) + "\n").encode("utf-8")); self.p.stdin.flush()

    def _read(self, want):
        while True:
            line = self.p.stdout.readline()
            if not line: raise RuntimeError("server closed")
            m = json.loads(line)
            if m.get("id") == want: return m

    def call(self, name, args):
        self.n += 1
        self._send({"jsonrpc": "2.0", "id": self.n, "method": "tools/call", "params": {"name": name, "arguments": args}})
        m = self._read(self.n)
        return m.get("result") or {"isError": True, "structuredContent": m.get("error")}

    def close(self):
        try: self.p.stdin.close(); self.p.wait(10)
        except Exception: self.p.kill()

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    calls = json.loads(sys.argv[1])
    limit = int(sys.argv[2]) if len(sys.argv) > 2 else 4000
    for (name, args), r in zip(calls, run(calls)):
        print("=====", name, json.dumps(args, ensure_ascii=False), "isError=", r and r.get("isError"))
        print(json.dumps(r and r.get("structuredContent"), indent=1, ensure_ascii=False)[:limit])

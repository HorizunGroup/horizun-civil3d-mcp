"""
Horizun Civil 3D MCP - live test of data shortcuts (needs the full_write profile and two owner actions).

  python scripts/verify_shortcuts.py project  <working_folder>   create/select project HZ_PRUEBA; prints where to SAVE AS
  (owner: SAVE AS the fixture drawing to the printed path)
  python scripts/verify_shortcuts.py publish                     publish HZ_EG (surface) and HZ_ROAD (alignment)
  (owner: NEW drawing from a Civil 3D template, keep it active)
  python scripts/verify_shortcuts.py reference                   reference both into the new drawing and re-read them
  python scripts/verify_shortcuts.py restore                     put back the previous working folder / project

State (previous folder, paths) is kept in %USERPROFILE%\\.horizun\\civil3d\\fixtures\\shortcuts-test.json.
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_call import run

DATA = os.path.join(os.environ["USERPROFILE"], ".horizun", "civil3d", "fixtures")
STATE = os.path.join(DATA, "shortcuts-test.json")
PROJECT = "HZ_PRUEBA"
EX = "horizun_c3d_exchange"
results = []


def sc(r): return (r or {}).get("structuredContent") or {}
def call(tool, args): return run([[tool, args]])[0]


def check(name, ok, detail=None):
    results.append((name, bool(ok)))
    print(("PASS " if ok else "FAIL ") + name + ("" if ok or detail is None else "  -> " + json.dumps(detail, ensure_ascii=False)[:700]))
    return ok


def doc():
    return sc(call("horizun_c3d_health", {}))["document"]["name"]


def wr(args, label):
    a = dict(args, target_document=doc())
    d = call(EX, a)
    tok = sc(d).get("confirmation_token")
    check(label + ": dry run", not d["isError"] and bool(tok), sc(d))
    if not tok: return None
    ap = call(EX, dict(a, dry_run=False, confirmation_token=tok))
    check(label + ": applied and re-read verified=match", not ap["isError"] and (sc(ap).get("verified") or {}).get("status") == "match", sc(ap))
    return sc(ap) if not ap["isError"] else None


def load(): return json.load(open(STATE, encoding="utf-8")) if os.path.exists(STATE) else {}
def save(s): os.makedirs(DATA, exist_ok=True); json.dump(s, open(STATE, "w", encoding="utf-8"), indent=1)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    phase = sys.argv[1] if len(sys.argv) > 1 else ""
    st = load()
    if phase == "project":
        folder = sys.argv[2]
        before = sc(call(EX, {"action": "shortcuts_status"}))
        st = {"previous_working_folder": before.get("working_folder"), "previous_project": before.get("current_project"), "working_folder": folder}
        r = wr({"action": "shortcuts_project", "working_folder": folder, "name": PROJECT, "description": "Prueba Horizun MCP"}, "create project " + PROJECT)
        if r:
            st["source_dwg"] = os.path.join(folder, PROJECT, "hz-fuente.dwg")
            save(st)
            print("\nGUARDAR COMO el dibujo de ensayo en:\n  " + st["source_dwg"])
    elif phase == "publish":
        s = sc(call(EX, {"action": "shortcuts_status"}))
        items = {e["name"]: e for e in s.get("publishable_from_this_drawing") or []}
        check("HZ_EG and HZ_ROAD are listed as publishable items of the saved fixture", "HZ_EG" in items and "HZ_ROAD" in items, s)
        todo = [n for n in ("HZ_EG", "HZ_ROAD") if n in items and not items[n].get("published")]
        if todo:
            wr({"action": "shortcuts_publish", "names": todo}, "publish " + " + ".join(todo))
        s = sc(call(EX, {"action": "shortcuts_status"}))
        pub = {(p["type"], p["name"]): p for p in s.get("published") or []}
        check("status lists both shortcuts, not broken", ("Surface", "HZ_EG") in pub and ("Alignment", "HZ_ROAD") in pub
              and not any(p["broken"] for p in pub.values()), s.get("published"))
    elif phase == "reference":
        wr({"action": "shortcuts_reference", "name": "HZ_EG", "type": "Surface"}, "reference surface HZ_EG")
        wr({"action": "shortcuts_reference", "name": "HZ_ROAD", "type": "Alignment"}, "reference alignment HZ_ROAD")
        z = sc(call("horizun_c3d_surface", {"action": "sample_elevation", "name": "HZ_EG", "points": [{"x": 25, "y": 75}]}))
        pz = (((z.get("surfaces") or [{}])[0]).get("points") or [{}])[0].get("elevation")
        check("referenced EG answers the plane value z(25,75) = 101.25", pz is not None and abs(pz - 101.25) < 1e-6, z)
        g = sc(call("horizun_c3d_alignment", {"action": "get", "name": "HZ_ROAD"})).get("alignment") or {}
        check("referenced HZ_ROAD: 90 m and reported not editable here (reference)", abs(sum(e.get("length") or 0 for e in g.get("entities") or []) - 90) < 1e-6
              and g.get("editable") is False, {k: g.get(k) for k in ("editable", "not_editable_because", "is_reference")})
    elif phase == "restore":
        prev_wf, prev_proj = st.get("previous_working_folder"), (st.get("previous_project") or "").rstrip("\/")
        if prev_wf:
            args = {"action": "shortcuts_project", "working_folder": prev_wf}
            if prev_proj: args["name"] = os.path.basename(prev_proj)
            wr(args, "restore previous working folder" + (" / project " + args["name"] if prev_proj else ""))
        else:
            print("No previous working folder was recorded: nothing to restore.")
    else:
        print(__doc__); return 2
    p = sum(1 for _, ok in results if ok)
    print(f"\n{p}/{len(results)} passed ({phase}).")
    return 0 if p == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())

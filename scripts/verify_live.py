"""
Horizun Civil 3D MCP - live verification against the deterministic fixture drawing.

Prerequisites (owner):
  1. Civil 3D open with the installed plug-in; NEW drawing from a Civil 3D template.
  2. Run HZ_BUILD_FIXTURE in that drawing (it refuses titled drawings or drawings with surfaces).
  3. python scripts/verify_live.py            (safe_write profile is enough; nothing is saved)

What it proves (each step PASS/FAIL, JSON report in %USERPROFILE%\\.horizun\\civil3d\\fixtures\\):
  reads    : health, surface get stats vs analytic values, sample_elevation incl. outside point,
             volumes_report (existing volume surface AND transient base/comparison) vs analytic cut/fill,
             transient volume leaves no persistent surface
  writes   : dry run changes nothing; apply with token -> verified=match; token reuse refused;
             stale plan refused after the drawing moved; rename/duplicate_style/set_style/create_tin/
             add_data (vertices + standard breakline + outer boundary)/paste/create_volume/rebuild
  refusals : locked layer, existing name, missing name, wrong target drawing, open polyline as boundary
It refuses to run unless the active drawing is the fixture recorded in fixture-expected.json.
"""
import json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_call import run

DATA = os.path.join(os.environ["USERPROFILE"], ".horizun", "civil3d", "fixtures")
EXPECTED = os.path.join(DATA, "fixture-expected.json")
results = []

def sc(r): return (r or {}).get("structuredContent") or {}
def call(tool, args): return run([[tool, args]])[0]

def check(name, ok, detail=None):
    results.append({"step": name, "pass": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + ("" if ok or detail is None else "  -> " + json.dumps(detail, ensure_ascii=False)[:600]))
    return ok

def close(a, b, tol): return a is not None and b is not None and abs(a - b) <= tol

def write(args, label):
    """dry run -> apply with token; returns (dry, applied)."""
    dry = call("horizun_c3d_surface", args)
    tok = sc(dry).get("confirmation_token")
    check(label + ": dry run returns plan + token, commits nothing",
          not dry["isError"] and tok and sc(dry).get("committed") is False, sc(dry))
    if not tok: return dry, None
    applied = call("horizun_c3d_surface", dict(args, dry_run=False, confirmation_token=tok))
    if applied["isError"] and sc(applied).get("confirmation_state") == "stale_plan":
        # Live finding: right after a previous write Civil 3D may update dependent objects in the
        # background; that IS a drawing change, so the token is refused. A well-behaved client
        # re-rehearses once - and the report says so.
        print("     note: " + label + " was stale (background update after the previous write); re-rehearsing once")
        results.append({"step": label + ": stale after previous write, re-rehearsed", "pass": True, "detail": "informational"})
        time.sleep(1)
        dry = call("horizun_c3d_surface", args)
        tok = sc(dry).get("confirmation_token")
        applied = call("horizun_c3d_surface", dict(args, dry_run=False, confirmation_token=tok))
    v = sc(applied).get("verified") or {}
    check(label + ": applied and re-read verified=match", not applied["isError"] and v.get("status") == "match", sc(applied))
    return dry, applied

def surface_names():
    r = call("horizun_c3d_surface", {"action": "list", "limit": 1000})
    return sorted(s.get("name") for s in sc(r).get("surfaces", []))

def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if not os.path.exists(EXPECTED):
        print("No fixture-expected.json: run HZ_BUILD_FIXTURE in a NEW drawing first."); return 2
    exp = json.load(open(EXPECTED, encoding="utf-8"))
    h = call("horizun_c3d_health", {})
    if h["isError"]: print("health failed:", sc(h)); return 2
    doc = sc(h)["document"]["name"]
    if os.path.basename(exp["document"]).lower() != doc.lower():
        print(f"Active drawing '{doc}' is not the fixture '{exp['document']}'. Refusing to write anywhere else."); return 2
    print(f"Fixture {doc}, plugin {sc(h)['plugin_version']}, contract {sc(h)['contract_hash']}")
    S, E = exp["surfaces"], exp["entities"]
    tgt = {"target_document": doc}

    # ---------------- reads ----------------
    g = sc(call("horizun_c3d_surface", {"action": "get", "name": "HZ_EG", "include_isopaca_statistics": False}))
    st = ((g.get("surfaces") or [{}])[0]).get("statistics") or {}
    check("EG stats: 121 points, area 10000, z 100..103",
          st.get("number_of_points") == 121 and close(st.get("area_2d"), 10000, 1e-6)
          and close(st.get("min_elevation"), 100, 1e-6) and close(st.get("max_elevation"), 103, 1e-6), st)

    pts = S["HZ_EG"]["samples"] + [S["HZ_EG"]["outside_sample"]]
    r = sc(call("horizun_c3d_surface", {"action": "sample_elevation", "name": "HZ_EG", "points": [{"x": p["x"], "y": p["y"]} for p in pts]}))
    rows = ((r.get("surfaces") or [{}])[0]).get("points") or []
    ok = len(rows) == 3 and all(close(rows[i].get("elevation"), pts[i]["z"], 1e-6) for i in range(2)) \
         and rows[2].get("elevation") is None and rows[2].get("status") == "outside_surface"
    check("sample_elevation: plane values exact, outside point = null (never 0)", ok, rows)

    v = S["HZ_EG_FG_VOL"]
    vr = sc(call("horizun_c3d_surface", {"action": "volumes_report", "name": "HZ_EG_FG_VOL"}))
    vol = (vr.get("surfaces") or [{}])[0]
    check(f"volume surface: cut {v['cut']:.3f} / fill {v['fill']:.3f} (analytic, tol 0.1%)",
          close(vol.get("unadjusted_cut"), v["cut"], v["cut"] * 1e-3) and close(vol.get("unadjusted_fill"), v["fill"], v["fill"] * 1e-3)
          and close(abs(vol.get("unadjusted_net") or 0), v["net_abs"], 5), vol)

    before = surface_names()
    tv = sc(call("horizun_c3d_surface", {"action": "volumes_report", "base": "HZ_EG", "comparison": "HZ_FG"}))
    tvol = tv.get("volume") or {}
    check("transient base/comparison volume matches analytic values",
          close(tvol.get("unadjusted_cut"), v["cut"], v["cut"] * 1e-3) and close(tvol.get("unadjusted_fill"), v["fill"], v["fill"] * 1e-3), tv)
    check("transient volume left no persistent surface", surface_names() == before, {"before": before, "after": surface_names()})

    # ---------------- writes ----------------
    dry, applied = write(dict(tgt, action="rename", name="HZ_FG", new_name="HZ_FG_R"), "rename")
    reuse = call("horizun_c3d_surface", dict(tgt, action="rename", name="HZ_FG", new_name="HZ_FG_R", dry_run=False,
                                            confirmation_token=sc(dry).get("confirmation_token")))
    check("token reuse refused", reuse["isError"], sc(reuse))
    write(dict(tgt, action="rename", name="HZ_FG_R", new_name="HZ_FG"), "rename back")

    # stale plan: rehearse, then move the drawing with another write, then apply the old token
    rename_eg = dict(tgt, action="rename", name="HZ_EG", new_name="HZ_EG_TMP")
    d1 = call("horizun_c3d_surface", rename_eg)
    write(dict(tgt, action="rename", name="HZ_FG", new_name="HZ_FG_TMP"), "intervening edit")
    late = call("horizun_c3d_surface", dict(rename_eg, dry_run=False, confirmation_token=sc(d1).get("confirmation_token")))
    check("stale plan refused after the drawing moved", late["isError"] and sc(late).get("code") == "confirmation_refused", sc(late))
    write(dict(tgt, action="rename", name="HZ_FG_TMP", new_name="HZ_FG"), "restore name")

    style = exp["style"]
    write(dict(tgt, action="duplicate_style", style=style, new_name="HZ_FIXTURE_STYLE_COPY"), "duplicate_style")
    write(dict(tgt, action="set_style", name="HZ_FG", style="HZ_FIXTURE_STYLE_COPY"), "set_style")

    write(dict(tgt, action="create_tin", new_name="HZ_SCRATCH", style=style), "create_tin")
    corners = [{"x": x, "y": y, "z": 100.0} for x, y in ((0, 0), (100, 0), (100, 100), (0, 100))]
    write(dict(tgt, action="add_data", name="HZ_SCRATCH", vertices=corners), "add_data vertices")
    write(dict(tgt, action="add_data", name="HZ_SCRATCH", breaklines={"handles": [E["breakline_3d"]["handle"]], "description": "fixture breakline"}),
          "add_data standard breakline (vertices re-read at z=110)")
    _, ab = write(dict(tgt, action="add_data", name="HZ_SCRATCH", boundaries={"handles": [E["outer_limit"]["handle"]], "kind": "outer", "name": "fixture limit"}),
                  "add_data outer boundary")
    area = (((sc(ab).get("after") or {}).get("counts")) or {}).get("area_2d") if ab else None
    check("outer boundary area = 6400 (80 x 80)", close(area, 6400, 1e-3), area)
    bad = call("horizun_c3d_surface", dict(tgt, action="add_data", name="HZ_SCRATCH", boundaries={"handles": [E["open_polyline"]["handle"]], "kind": "outer"}))
    check("open polyline refused as boundary", bad["isError"] and "not closed" in json.dumps(sc(bad)), sc(bad))

    write(dict(tgt, action="create_tin", new_name="HZ_PASTE", style=style), "create_tin for paste")
    write(dict(tgt, action="paste", name="HZ_PASTE", sources=["HZ_EG"]), "paste HZ_EG")
    ps = sc(call("horizun_c3d_surface", {"action": "sample_elevation", "name": "HZ_PASTE", "points": [{"x": 25, "y": 75}]}))
    pz = (((ps.get("surfaces") or [{}])[0]).get("points") or [{}])[0].get("elevation")
    check("pasted surface carries EG elevations (25,75)", close(pz, S["HZ_EG"]["samples"][0]["z"], 1e-6), pz)

    _, cv = write(dict(tgt, action="create_volume", new_name="HZ_EG_FG_VOL2", base="HZ_EG", comparison="HZ_FG", style=style), "create_volume")
    cvol = ((sc(cv).get("actual") or [{}])[0]).get("volume") if cv else None
    check("created volume read back from the new object matches analytic",
          bool(cvol) and close(cvol.get("unadjusted_cut"), v["cut"], v["cut"] * 1e-3), cvol)
    write(dict(tgt, action="rebuild", name="HZ_EG"), "rebuild")

    # ---------------- refusals ----------------
    for label, args, code in [
        ("locked layer refused", dict(tgt, action="rename", name="HZ_LOCKED", new_name="HZ_X"), "not_editable"),
        ("existing destination name refused", dict(tgt, action="rename", name="HZ_FG", new_name="HZ_EG"), "invalid_input"),
        ("missing surface refused with candidates", dict(tgt, action="rename", name="HZ_NOPE", new_name="HZ_X"), "not_found"),
        ("wrong target drawing refused", dict(action="rename", name="HZ_FG", new_name="HZ_X", target_document="Other.dwg"), "document_mismatch"),
    ]:
        r = call("horizun_c3d_surface", args)
        check(label, r["isError"] and sc(r).get("code") == code, sc(r))

    # ---------------- analysis (v0.4.0) ----------------
    _, ea = write(dict(tgt, action="apply_elevation_analysis", name="HZ_EG_FG_VOL", mode="step", interval=0.5, break_at=0,
                       color_scheme="cutfill"), "elevation analysis step 0.5 on the volume surface")
    stats = (((sc(ea).get("actual") or [{}])[0]).get("band_statistics") or {}) if ea else {}
    bands = stats.get("bands") or []
    check("elevation bands: 6 bands from -2 to 1, none straddling 0",
          len(bands) == 6 and close(bands[0].get("min"), -2, 1e-9) and close(bands[-1].get("max"), 1, 1e-9)
          and not any(b["min"] < 0 < b["max"] for b in bands), bands)
    rec = stats.get("reconciliation") or {}
    check("sum of band volumes = native net -5000 m3 (tol 0.5%)", rec.get("agree") is True and close(rec.get("bands_estimate"), -5000, 25), rec)

    write(dict(tgt, action="apply_elevation_analysis", name="HZ_EG", mode="ranges",
               ranges=[{"min": 100, "max": 101, "color": 3}, {"min": 101, "max": 102, "color": 2}, {"min": 102, "max": 103, "color": 1}]),
          "elevation analysis explicit ranges")
    write(dict(tgt, action="apply_elevation_analysis", name="HZ_EG", mode="recolor", colors=["#0000FF", "#00FF00", "#FF0000"]),
          "elevation analysis recolor")
    bad = call("horizun_c3d_surface", dict(tgt, action="apply_elevation_analysis", name="HZ_EG", mode="step", interval=1, colors=[1, 2]))
    check("colour count that does not match the bands is refused", bad["isError"] and sc(bad).get("code") == "invalid_input", sc(bad))

    shared = call("horizun_c3d_surface", dict(tgt, action="style_display", style=style, components={"slopes": {"visible": True}}))
    check("style_display on a SHARED style refused without allow_shared_style", shared["isError"] and "used by" in json.dumps(sc(shared)), sc(shared))
    write(dict(tgt, action="duplicate_style", style=style, new_name="HZ_SLOPE_STYLE"), "duplicate style for slopes")
    write(dict(tgt, action="style_display", style="HZ_SLOPE_STYLE", components={"slopes": {"visible": True}, "border": {"visible": True, "color": "#FF00FF"}}),
          "style_display: slopes + magenta border")
    write(dict(tgt, action="set_style", name="HZ_EG", style="HZ_SLOPE_STYLE"), "assign slope style to EG")
    slope_ranges = [{"min": 0, "max": 2, "color": "#00FF00"}, {"min": 2, "max": 5, "color": "#FFFF00"}, {"min": 5, "max": 10, "color": "#FF8000"},
                    {"min": 10, "max": 30, "color": "#FF0000"}, {"min": 30, "max": 100, "color": "#800080"}]
    _, sa = write(dict(tgt, action="apply_slope_analysis", name="HZ_EG", mode="ranges", ranges=slope_ranges), "slope analysis 0-2-5-10-30-100 %")
    sstats = (((sc(sa).get("actual") or [{}])[0]).get("band_statistics") or {}) if sa else {}
    sb = sstats.get("bands") or []
    check("EG plane slope 2.236 %: 100 % of the area in the 2-5 % band (proves the slope unit)",
          len(sb) == 5 and close(sb[1].get("area_pct"), 100, 0.01), sb)

    # ---------------- feature lines (v0.5.0) ----------------
    def fl(args, label):
        dry = call("horizun_c3d_feature_line", dict(tgt, **args))
        tok = sc(dry).get("confirmation_token")
        check(label + ": dry run returns plan + token", not dry["isError"] and bool(tok), sc(dry))
        if not tok: return None
        ap = call("horizun_c3d_feature_line", dict(tgt, **args, dry_run=False, confirmation_token=tok))
        check(label + ": applied and re-read verified=match", not ap["isError"] and (sc(ap).get("verified") or {}).get("status") == "match", sc(ap))
        return sc(ap)
    brk = E["breakline_3d"]["handle"]
    fl({"action": "create_from_polyline", "handles": [brk], "names": ["HZ_FL1"]}, "feature line from 3D polyline (XYZ re-read)")
    dup = call("horizun_c3d_feature_line", dict(tgt, action="create_from_polyline", handles=[E["open_polyline"]["handle"]], names=["HZ_FL1"]))
    check("duplicate feature-line name refused", dup["isError"] and "already exists" in json.dumps(sc(dup)), sc(dup))
    r = fl({"action": "set_elevations", "name": "HZ_FL1", "mode": "from_surface", "surface": "HZ_EG"}, "feature line elevations from EG")
    pts = ((r or {}).get("after") or [{}])[-1].get("points") or {}
    check("feature line on EG: z from 100.9 to 102.1 (analytic)", close(pts.get("min_z"), 100.9, 1e-3) and close(pts.get("max_z"), 102.1, 1e-3), pts)
    fl({"action": "set_elevations", "name": "HZ_FL1", "mode": "constant", "elevation": 105}, "feature line constant elevation 105")
    fl({"action": "set_elevations", "name": "HZ_FL1", "mode": "points", "points": [{"index": 0, "z": 106}]}, "feature line point 0 to 106")
    fl({"action": "rename", "name": "HZ_FL1", "new_name": "HZ_FL1_R"}, "feature line rename")
    fl({"action": "export_polyline3d", "name": "HZ_FL1_R", "layer": "HZ-FIXTURE-DATA"}, "feature line export to 3D polyline")

    # ---------------- geometric grading (v0.5.0) ----------------
    G = E.get("platform") or {}
    gargs = dict(tgt, action="create_geometric", source=G.get("handle", "0"), surface="HZ_EG", name="HZ_PLATFORM",
                 outer=[{"type": "grade_to_surface", "slope": 2}], volume_against="HZ_EG")
    gd = call("horizun_c3d_grading", gargs)
    lines = sc(gd).get("plan", {}).get("lines") or []
    check("grading dry run: base + daylight lines, no failed rays",
          not gd["isError"] and len(lines) == 2 and (sc(gd).get("plan", {}).get("daylight_rays") or {}).get("failed") == 0, sc(gd))
    ga = call("horizun_c3d_grading", dict(gargs, dry_run=False, confirmation_token=sc(gd).get("confirmation_token")))
    check("grading applied: TIN + breaklines + boundary + daylight vertices on EG verified",
          not ga["isError"] and (sc(ga).get("verified") or {}).get("status") == "match", sc(ga))
    va = (sc(ga).get("after") or {}).get("volume_against") or {}
    check("grading volume vs EG: all fill (> 1000 m3 under the platform alone), cut ~ 0",
          (va.get("unadjusted_fill") or 0) > 1000 and (va.get("unadjusted_cut") or 99) < 1, va)
    ps = sc(call("horizun_c3d_surface", {"action": "sample_elevation", "name": "HZ_PLATFORM", "points": [{"x": 50, "y": 50}, {"x": 50, "y": 60}]}))
    pz = [p.get("elevation") for p in (((ps.get("surfaces") or [{}])[0]).get("points") or [])]
    check("platform TIN at 104 inside and on the edge", len(pz) == 2 and all(close(z, 104, 1e-6) for z in pz), pz)
    op = call("horizun_c3d_grading", dict(gargs, source=E["open_polyline"]["handle"], name="HZ_X"))
    check("open polyline refused as grading source", op["isError"] and "not closed" in json.dumps(sc(op)), sc(op))
    col = call("horizun_c3d_grading", dict(tgt, action="create_geometric", source=G.get("handle", "0"), name="HZ_X", inner=[{"type": "offset", "dist": 15}]))
    check("collapsing inner offset refused at dry run", col["isError"] and "collapses" in json.dumps(sc(col)), sc(col))

    # ---------------- C# escape hatch is OFF by default ----------------
    cs = call("horizun_c3d_execute_csharp", {"code": "return db.Filename;", "target_document": doc})
    if cs["isError"]:
        check("execute_csharp refused under the default profile (off by default)", sc(cs).get("code") == "permission_denied", sc(cs))
    else:
        check("execute_csharp query mode (owner enabled it): read-only, self-reported",
              sc(cs).get("committed") is False and sc(cs).get("host_verified") is False, sc(cs))

    # ---------------- undo (one MCP write = one UNDO step) ----------------
    write(dict(tgt, action="rename", name="HZ_PASTE", new_name="HZ_PASTE_U"), "rename for undo check")
    # undo_last: the bridge's own, guarded UNDO of its last write (refused if anything changed since)
    ud = call("horizun_c3d_document", dict(tgt, action="undo_last"))
    utok = sc(ud).get("confirmation_token")
    check("undo_last dry run names the last write (surface rename)", not ud["isError"] and bool(utok)
          and (sc(ud).get("plan") or {}).get("undo", {}).get("action") == "rename", sc(ud))
    if utok:
        ua = call("horizun_c3d_document", dict(tgt, action="undo_last", dry_run=False, confirmation_token=utok))
        check("undo_last applied", not ua["isError"], sc(ua))
    check("UNDO reverted the last MCP write as one step (HZ_PASTE_U -> HZ_PASTE)",
          "HZ_PASTE" in surface_names() and "HZ_PASTE_U" not in surface_names(), surface_names())
    again = call("horizun_c3d_document", dict(tgt, action="undo_last"))
    check("a second undo_last is refused (one level only, never someone else's step)", again["isError"], sc(again))

    # ---------------- PHASE3 action tools (blocks A-D, after the UNDO check: a fatal abort in a block must not hide it) ----------------
    import verify_blocks
    verify_blocks.run_all(call, check, sc, tgt, exp)

    passed = sum(1 for r in results if r["pass"])
    report = {"utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "drawing": doc,
              "plugin": sc(h).get("plugin_version"), "contract": sc(h).get("contract_hash"),
              "passed": passed, "total": len(results), "steps": results}
    os.makedirs(DATA, exist_ok=True)
    out = os.path.join(DATA, "verify-" + time.strftime("%Y%m%d-%H%M%S") + ".json")
    json.dump(report, open(out, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print(f"\n{passed}/{len(results)} passed. Report: {out}")
    return 0 if passed == len(results) else 1

if __name__ == "__main__":
    sys.exit(main())

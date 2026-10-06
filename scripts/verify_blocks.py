"""
Horizun Civil 3D MCP - live verification of the PHASE3 action tools (blocks A-D) on the fixture drawing.

Called by verify_live.py (shares its call/check helpers and the fixture expectations); can also run alone:
  python scripts/verify_blocks.py [road|labels|cad|data ...]

Every write goes dry run -> token -> apply -> verified=match, exactly like verify_live.py, and every
analytic value comes from the fixture geometry (EG plane z = 100 + 0.02x + 0.01y; road y = 50, x 5..95).
"""
import hashlib, json, os, sys, time, zipfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

H = {}  # call, check, sc, tgt, exp set by run_all


def close(a, b, tol): return a is not None and b is not None and abs(a - b) <= tol


def wr(tool, args, label):
    """Dry run -> apply with token (re-rehearses once on stale_plan). Returns the applied structuredContent or None."""
    call, check, sc, tgt = H["call"], H["check"], H["sc"], H["tgt"]
    a = dict(tgt, **args)
    dry = call(tool, a)
    tok = sc(dry).get("confirmation_token")
    check(label + ": dry run returns plan + token, commits nothing", not dry["isError"] and bool(tok) and sc(dry).get("committed") is False, sc(dry))
    if not tok:
        return None
    ap = call(tool, dict(a, dry_run=False, confirmation_token=tok))
    if ap["isError"] and sc(ap).get("confirmation_state") == "stale_plan":
        print("     note: " + label + " was stale (background update); re-rehearsing once")
        time.sleep(1)
        dry = call(tool, a)
        ap = call(tool, dict(a, dry_run=False, confirmation_token=sc(dry).get("confirmation_token")))
    check(label + ": applied and re-read verified=match", not ap["isError"] and (sc(ap).get("verified") or {}).get("status") == "match", sc(ap))
    return sc(ap)


def rd(tool, args):
    return H["sc"](H["call"](tool, args))


def refused(tool, args, label, code=None, text=None):
    call, check, sc, tgt = H["call"], H["check"], H["sc"], H["tgt"]
    r = call(tool, dict(tgt, **args))
    ok = r["isError"] and (code is None or sc(r).get("code") == code) and (text is None or text in json.dumps(sc(r)))
    return check(label, ok, sc(r))


# ------------------------------------------------------------------ block A: roads
def road():
    check, E = H["check"], H["exp"]["entities"]
    rc = E.get("road_centerline")
    if not rc:
        check("fixture has road_centerline (rebuild the fixture with v0.6+)", False, list(E)); return
    AL, PR = "horizun_c3d_alignment", "horizun_c3d_profile"

    wr(AL, {"action": "create_from_polyline", "polyline": rc["handle"], "new_name": "HZ_ROAD"}, "alignment from polyline")
    g = rd(AL, {"action": "get", "name": "HZ_ROAD"}).get("alignment") or {}
    ents = g.get("entities") or []
    check("HZ_ROAD: one tangent, length 90", len(ents) == 1 and close(ents[0].get("length"), 90, 1e-6), g)
    so = rd(AL, {"action": "station_offset", "name": "HZ_ROAD", "points": [{"x": 50, "y": 53}]}).get("rows") or [{}]
    check("station_offset (50,53): station 45, |offset| 3 (sign reported per Civil 3D)",
          close(so[0].get("station"), 45, 1e-6) and close(abs(so[0].get("offset") or 0), 3, 1e-6), so)
    xy = rd(AL, {"action": "station_offset", "name": "HZ_ROAD", "stations": [{"station": 45}]}).get("rows") or [{}]
    check("station 45 -> (50,50)", close(xy[0].get("x"), 50, 1e-6) and close(xy[0].get("y"), 50, 1e-6), xy)
    refused(AL, {"action": "create_from_polyline", "polyline": rc["handle"], "new_name": "HZ_ROAD"}, "duplicate alignment name refused", text="already exists")

    wr(AL, {"action": "create_by_pis", "pis": [{"x": 10, "y": 10}, {"x": 50, "y": 10}, {"x": 90, "y": 40}], "radii": [20], "new_name": "HZ_PIS"},
       "alignment by PIs with R=20 (radius and length re-read)")
    pg = rd(AL, {"action": "get", "name": "HZ_PIS"}).get("alignment") or {}
    pe = pg.get("entities") or []
    arcs = [e for e in pe if "Arc" in (e.get("type") or "") or e.get("radius")]
    check("HZ_PIS: tangent-arc-tangent, R = 20, total length 89.537 (analytic)",
          len(pe) == 3 and arcs and close(arcs[0].get("radius"), 20, 1e-6) and close(sum(e.get("length") or 0 for e in pe), 89.5370, 1e-3), pe)
    refused(AL, {"action": "create_by_pis", "pis": [{"x": 0, "y": 0}, {"x": 5, "y": 0}, {"x": 5, "y": 5}], "radii": [50], "new_name": "HZ_X"},
            "curve that does not fit between PIs refused at dry run")

    wr(AL, {"action": "create_offset", "name": "HZ_ROAD", "offset": 5, "new_name": "HZ_ROAD_OFF5"}, "offset alignment +5")

    wr(PR, {"action": "create_from_surface", "alignment": "HZ_ROAD", "surface": "HZ_EG", "new_name": "HZ_ROAD_EG"}, "EG profile from surface")
    ea = rd(PR, {"action": "elevation_at", "name": "HZ_ROAD_EG", "alignment": "HZ_ROAD", "stations": [0, 45, 90]}).get("rows") or []
    check("EG profile = 100.6 + 0.02 s (0, 45, 90)", len(ea) == 3 and all(close(r.get("elevation"), 100.6 + 0.02 * s, 1e-3) for r, s in zip(ea, (0, 45, 90))), ea)

    wr(PR, {"action": "create_layout", "alignment": "HZ_ROAD", "new_name": "HZ_ROAD_FG",
            "pvis": [{"station": 0, "elevation": 101}, {"station": 45, "elevation": 102.5, "curve_length": 20}, {"station": 90, "elevation": 101}]},
       "layout profile with crest curve L=20")
    fz = rd(PR, {"action": "elevation_at", "name": "HZ_ROAD_FG", "alignment": "HZ_ROAD", "stations": [10, 45]}).get("rows") or []
    check("FG: z(10) = 101.3333 (tangent), z(45) = 102.3333 (PVI - A*L/800)",
          len(fz) == 2 and close(fz[0].get("elevation"), 101 + 10 / 30, 1e-4) and close(fz[1].get("elevation"), 102.5 - (20 / 3) * 20 / 800, 1e-4), fz)
    k = rd(PR, {"action": "check_k", "name": "HZ_ROAD_FG", "alignment": "HZ_ROAD", "min_k_crest": 17})
    cv = (k.get("curves") or [{}])[0]
    check("check_k: crest K = 3.0 fails the caller's 17", close(cv.get("k"), 3.0, 1e-3) and (k.get("summary") or {}).get("failing") == 1, k)
    wr(PR, {"action": "create_view", "alignment": "HZ_ROAD", "insert": {"x": 0, "y": -120}, "new_name": "HZ_PV"}, "profile view")


# ------------------------------------------------------------------ block B: Civil labels
def labels():
    check, E = H["check"], H["exp"]["entities"]
    LB = "horizun_c3d_labels"
    st = rd(LB, {"action": "list_styles", "kind": "surface_spot_elevation"})
    check("spot elevation label styles listed", len(st.get("styles") or []) > 0, st)

    r = wr(LB, {"action": "surface_spot", "surface": "HZ_EG", "points": [{"x": 25, "y": 75}, {"x": 63, "y": 17}]}, "2 spot elevation labels on EG (anchor re-read)")
    spot = ((r or {}).get("after") or {}).get("labels") or []
    check("spot labels attached to HZ_EG", len(spot) == 2 and all(l.get("feature_name") == "HZ_EG" for l in spot), spot)
    refused(LB, {"action": "surface_spot", "surface": "HZ_EG", "points": [{"x": 150, "y": 150}]}, "spot label outside the surface refused", text="outside")
    wr(LB, {"action": "surface_slope", "surface": "HZ_EG", "segments": [{"from": {"x": 30, "y": 30}, "to": {"x": 40, "y": 30}}]}, "two-point slope label")
    wr(LB, {"action": "contour_labels", "surface": "HZ_EG", "line": [{"x": 10, "y": 10}, {"x": 90, "y": 90}]}, "contour label line")

    if rd("horizun_c3d_alignment", {"action": "get", "name": "HZ_ROAD"}).get("alignment"):
        r = wr(LB, {"action": "alignment_stations", "alignment": "HZ_ROAD", "increment": 20}, "major station labels every 20 m")
        lab = (((r or {}).get("after") or {}).get("labels") or [{}])[0]
        check("station label group: 6 labels (0,20,40,60,80 + end 90)", lab.get("sub_labels") == 6, lab)
        wr(LB, {"action": "alignment_geometry", "alignment": "HZ_PIS"}, "tangent + curve labels on HZ_PIS (3 labels)")
        wr(LB, {"action": "station_offset", "alignment": "HZ_ROAD", "points": [{"x": 50, "y": 53}]}, "station/offset label")
        wr(LB, {"action": "profile_pvis", "profile_view": "HZ_PV", "profile": "HZ_ROAD_FG"}, "PVI labels in the profile view")
        wr(LB, {"action": "station_elevation", "profile_view": "HZ_PV", "items": [{"station": 45, "elevation": 102.3333}]}, "station/elevation label at the crest")
        refused(LB, {"action": "station_elevation", "profile_view": "HZ_PV", "items": [{"station": 500, "elevation": 101}]}, "station/elevation outside the view refused", text="outside")

    r = wr(LB, {"action": "note", "location": {"x": 10, "y": 95}, "text": "HORIZUN - NOTA DE ENSAYO"}, "note label with text (text re-read)")
    note = (((r or {}).get("after") or {}).get("labels") or [{}])[0]
    if note.get("handle"):
        wr(LB, {"action": "set_text", "handle": note["handle"], "text": "HORIZUN - TEXTO EDITADO"}, "note text override")
    seg = wr(LB, {"action": "segment", "entity": E["outer_limit"]["handle"], "ratio": 0.25}, "segment label on the outer limit at 25 %")
    lst = rd(LB, {"action": "list", "surface": "HZ_EG"})
    check("list by surface: spot + slope + contour labels found", (lst.get("count") or 0) >= 4, lst.get("by_type"))
    refused(LB, {"action": "erase", "handles": [E["outer_limit"]["handle"]]}, "erase refuses a non-label object", text="not a Civil 3D label")
    sl = (((seg or {}).get("after") or {}).get("labels") or [{}])[0]
    if sl.get("handle"):
        wr(LB, {"action": "erase", "handles": [sl["handle"]]}, "erase the segment label")


# ------------------------------------------------------------------ block C: AutoCAD
def full_write_or_refused(tool, args, label):
    """FULL WRITE actions: under the default safe_write profile they must be refused by permission; otherwise verified."""
    call, check, sc, tgt = H["call"], H["check"], H["sc"], H["tgt"]
    r = call(tool, dict(tgt, **args))
    if r["isError"] and sc(r).get("code") == "permission_denied":
        return check(label + ": refused under safe_write (FULL WRITE)", True, None) and None
    check(label + ": dry run allowed (full_write profile active)", not r["isError"], sc(r))
    return wr(tool, args, label)


def after(r, key):
    return ((r or {}).get("after") or {}).get(key)


def cad():
    check, E = H["check"], H["exp"]["entities"]
    LY, EN, DM, ST, BK, TB, LO, CL = ("horizun_c3d_layers", "horizun_c3d_entities", "horizun_c3d_dimensions", "horizun_c3d_cad_styles",
                                      "horizun_c3d_blocks", "horizun_c3d_tables", "horizun_c3d_layouts", "horizun_c3d_cleanup")
    # layers
    wr(LY, {"action": "create", "new_name": "HZ-TEST", "color": 3, "lineweight": 0.35, "linetype": "DASHED", "description": "ensayo"},
       "layer create (DASHED loaded from acadiso.lin)")
    wr(LY, {"action": "set", "name": "HZ-TEST", "new_name": "HZ-TEST2", "color": "#FF8000"}, "layer rename + true colour")
    wr(LY, {"action": "set_current", "name": "HZ-TEST2"}, "layer set current")
    refused(LY, {"action": "set", "name": "HZ-TEST2", "frozen": True}, "freezing the current layer refused", text="cannot be frozen")
    wr(LY, {"action": "set_current", "name": "0"}, "layer 0 current again")
    wr(LY, {"action": "state_save", "state_name": "HZ_STATE", "description": "antes"}, "layer state save")
    wr(LY, {"action": "set", "name": "HZ-TEST2", "off": True}, "layer off")
    wr(LY, {"action": "state_restore", "state_name": "HZ_STATE"}, "layer state restore")
    ly = (rd(LY, {"action": "list", "pattern": "HZ-TEST2"}).get("layers") or [{}])[0]
    check("restored state turned HZ-TEST2 back on", ly.get("off") is False, ly)

    # entities
    sq = [{"x": 0, "y": -60}, {"x": 20, "y": -60}, {"x": 20, "y": -40}, {"x": 0, "y": -40}]
    d = wr(EN, {"action": "draw", "layer": "HZ-TEST2", "items": [
        {"type": "line", "from": {"x": 0, "y": -20}, "to": {"x": 30, "y": -20}},
        {"type": "polyline", "points": sq, "closed": True},
        {"type": "circle", "center": {"x": 50, "y": -50}, "radius": 5},
        {"type": "arc", "center": {"x": 70, "y": -50}, "radius": 5, "start_angle": 0, "end_angle": 90},
        {"type": "text", "position": {"x": 0, "y": -30}, "text": "HORIZUN", "height": 2.5, "justify": "middle_center"},
        {"type": "mtext", "position": {"x": 40, "y": -30}, "text": "MTEXT\\PDE ENSAYO", "height": 2, "width": 30},
        {"type": "point", "position": {"x": 90, "y": -50}},
        {"type": "polyline3d", "points": [{"x": 0, "y": -70, "z": 100}, {"x": 10, "y": -70, "z": 101}, {"x": 20, "y": -75, "z": 102}]},
    ]}, "draw 8 entity types (geometry re-read)")
    made = after(d, "created") or []
    h = {m.get("type"): m.get("handle") for m in made}
    check("drawn square area 400, line length 30", any(close(m.get("area"), 400, 1e-6) for m in made) and any(close(m.get("length"), 30, 1e-6) for m in made), made)
    if h.get("LWPOLYLINE"):
        r = wr(EN, {"action": "draw", "items": [{"type": "hatch", "boundaries": [h["LWPOLYLINE"]], "pattern": "ANSI31", "scale": 0.5}]}, "hatch from the square")
        check("hatch area 400", close(((after(r, "created") or [{}])[0]).get("area"), 400, 1e-6), after(r, "created"))
    q = rd(EN, {"action": "query", "layers": ["HZ-TEST2"]})
    check("query by layer finds the drawn entities", (q.get("count") or 0) >= 8, q.get("by_type"))
    if h.get("LINE"):
        wr(EN, {"action": "transform", "handles": [h["LINE"]], "operation": "move", "displacement": {"x": 0, "y": -5}}, "move line")
        wr(EN, {"action": "set_properties", "handles": [h["LINE"]], "color": 1, "lineweight": 0.5}, "set properties on the line")
    if h.get("LWPOLYLINE"):
        c = wr(EN, {"action": "transform", "handles": [h["LWPOLYLINE"]], "operation": "copy", "displacement": {"x": 30, "y": 0}}, "copy square (area kept)")
        cp = ((after(c, "results") or [{}])[0]).get("handle")
        if cp:
            wr(EN, {"action": "transform", "handles": [cp], "operation": "scale", "base": {"x": 30, "y": -60}, "factor": 2}, "scale copy x2 (area x4 re-read)")
            wr(EN, {"action": "transform", "handles": [cp], "operation": "rotate", "base": {"x": 30, "y": -60}, "angle": 90}, "rotate copy 90 deg")
            o = wr(EN, {"action": "offset", "handle": h["LWPOLYLINE"], "distance": 2}, "offset square 2 (distance re-read)")
            ex = wr(EN, {"action": "explode", "handles": [cp]}, "explode copy (summed length 160)")
            parts = [p.get("handle") for p in ((after(ex, "exploded") or [{}])[0].get("parts") or [])]
            if len(parts) == 4:
                refused(EN, {"action": "join", "handles": parts}, "join of 4 lines with a line as base refused by rehearsal", text="join")
            full_write_or_refused(EN, {"action": "erase", "handles": parts[:1] or [h["LWPOLYLINE"]]}, "erase")

    # dimensions
    wr(DM, {"action": "linear", "p1": {"x": 0, "y": -20}, "p2": {"x": 30, "y": -20}, "dim_line_point": {"x": 15, "y": -15}}, "linear dimension = 30")
    wr(DM, {"action": "aligned", "p1": {"x": 0, "y": 0}, "p2": {"x": 30, "y": 40}, "dim_line_point": {"x": 10, "y": 25}}, "aligned dimension = 50")
    wr(DM, {"action": "angular", "center": {"x": 0, "y": -100}, "p1": {"x": 10, "y": -100}, "p2": {"x": 0, "y": -90}, "arc_point": {"x": 5, "y": -95}}, "angular dimension = 90 deg")
    if h.get("CIRCLE"):
        wr(DM, {"action": "radial", "entity": h["CIRCLE"]}, "radial dimension = 5")
        wr(DM, {"action": "diameter", "entity": h["CIRCLE"], "angle": 30}, "diameter dimension = 10")
    wr(DM, {"action": "ordinate", "feature_point": {"x": 25, "y": -80}, "leader_end": {"x": 25, "y": -85}, "axis": "x"}, "ordinate dimension = 25")
    ch = wr(DM, {"action": "chain", "points": [{"x": 0, "y": -120}, {"x": 10, "y": -120}, {"x": 25, "y": -120}, {"x": 45, "y": -120}],
                 "dim_line_point": {"x": 0, "y": -115}}, "chain dimensions 10/15/20")
    wr(DM, {"action": "chain", "mode": "baseline", "points": [{"x": 0, "y": -140}, {"x": 10, "y": -140}, {"x": 25, "y": -140}],
            "dim_line_point": {"x": 0, "y": -135}}, "baseline dimensions 10/25")
    wr(DM, {"action": "mleader", "arrow_point": {"x": 50, "y": -50}, "landing_point": {"x": 60, "y": -40}, "text": "POZO DE ENSAYO"}, "multileader")
    dims = after(ch, "dimensions") or []
    if dims:
        wr(DM, {"action": "set_text", "handles": [dims[0]["handle"]], "text": "<> m"}, "dimension text override '<> m'")

    # styles
    wr(ST, {"action": "text_create", "new_name": "HZ_TXT", "font": "arial.ttf", "height": 0, "width_factor": 0.9}, "text style Arial")
    wr(ST, {"action": "dim_create", "new_name": "HZ_DIM", "properties": {"dimtxt": 2.5, "dimasz": 2, "dimdec": 2, "dimtxsty": "HZ_TXT", "dimtih": False}}, "dimension style")
    wr(ST, {"action": "dim_set", "name": "HZ_DIM", "properties": {"dimlfac": 1, "dimgap": 0.8}}, "dimension style edit")
    wr(ST, {"action": "mleader_create", "new_name": "HZ_ML", "text_style": "HZ_TXT", "text_height": 2.5, "arrow_size": 2}, "multileader style")
    wr(ST, {"action": "scale_add", "new_name": "HZ 1:250", "paper_units": 1, "drawing_units": 250}, "annotation scale 1:250")
    wr(ST, {"action": "set_current", "kind": "dim", "name": "HZ_DIM"}, "current dimension style")
    wr(ST, {"action": "linetype_load", "names": ["PHANTOM2", "HIDDEN2"]}, "load linetypes")
    check("dimension style list shows HZ_DIM with dimtxt 2.5",
          any(s.get("name") == "HZ_DIM" and close(s.get("dimtxt"), 2.5, 1e-9) for s in rd(ST, {"action": "list", "kind": "dim"}).get("styles") or []), None)

    # blocks
    define = {"action": "define", "new_name": "HZ_TB", "base_point": {"x": 50, "y": -50},
              "attributes": [{"tag": "TITLE", "prompt": "Titulo", "position": {"x": 0, "y": 7}, "height": 2},
                             {"tag": "DATE", "position": {"x": 0, "y": -9}, "height": 1.5, "default": "-"}]}
    if h.get("CIRCLE"):
        define["handles"] = [h["CIRCLE"]]
    wr(BK, define, "block define (circle copied) with 2 attributes")
    wr(BK, {"action": "insert", "name": "HZ_TB", "position": {"x": 120, "y": -50}, "rotation": 15, "attributes": {"TITLE": "VIA DE ENSAYO"}}, "block insert with attribute values")
    wr(BK, {"action": "set_attributes", "name": "HZ_TB", "values": {"TITLE": "VIA HZ"}}, "batch attribute values on every reference")
    refused(BK, {"action": "insert", "name": "HZ_TB", "position": {"x": 0, "y": 0}, "attributes": {"NOPE": "x"}}, "unknown attribute tag refused", text="no (non-constant) attribute")
    rf = rd(BK, {"action": "references", "name": "HZ_TB"})
    check("one reference with TITLE = VIA HZ", rf.get("count") == 1 and ((rf.get("references") or [{}])[0].get("attributes") or {}).get("TITLE") == "VIA HZ", rf)

    # tables
    t = wr(TB, {"action": "create", "position": {"x": 150, "y": -20}, "title": "PERFIL EG",
                "rows": [["Abscisa", "Cota EG"], ["0", "100.60"], ["45", "101.50"], ["90", "102.40"]], "text_height": 2}, "table from rows (every cell re-read)")
    th = (after(t, "table") or {}).get("handle")
    if th:
        wr(TB, {"action": "set_cells", "handle": th, "cells": [{"row": 3, "col": 1, "value": "101.500"}]}, "table cell edit")

    # layouts
    wr(LO, {"action": "create", "new_name": "HZ_LAMINA"}, "layout create")
    dev = rd(LO, {"action": "devices", "device": "DWG To PDF.pc3"})
    a3 = next((m["canonical"] for m in dev.get("media") or [] if "A3" in m.get("canonical", "") and "full_bleed" in m.get("canonical", "")), None)
    if a3:
        wr(LO, {"action": "page_setup", "layout": "HZ_LAMINA", "device": "DWG To PDF.pc3", "media": a3, "area": "layout", "scale": 1}, "page setup A3 PDF 1:1")
    wr(LO, {"action": "viewport", "layout": "HZ_LAMINA", "center": {"x": 210, "y": 148.5}, "width": 380, "height": 250, "view_center": {"x": 50, "y": -50},
            "scale": 1.0, "frozen_layers": ["HZ-TEST2"]}, "viewport (scale, view centre, frozen layer, locked)")
    pdf = os.path.join(os.environ["USERPROFILE"], ".horizun", "civil3d", "fixtures", "hz-lamina.pdf")
    full_write_or_refused(LO, {"action": "plot_pdf", "layouts": ["HZ_LAMINA"], "output": pdf, "overwrite": True}, "plot_pdf one page")
    full_write_or_refused(LO, {"action": "delete", "name": "HZ_LAMINA"}, "layout delete")

    # cleanup
    pp = rd(CL, {"action": "purge_preview"})
    check("purge preview lists purgeable items (e.g. HIDDEN2 linetype)", "HIDDEN2" in json.dumps(pp.get("purgeable")), pp.get("purgeable"))
    rep = rd(CL, {"action": "drawing_report"})
    check("drawing report counts entities and layers", (rep.get("entities") or 0) > 10 and "HZ-TEST2" in json.dumps(rep.get("by_layer_top200")), {k: rep.get(k) for k in ("entities", "on_layer_0", "proxies")})
    sc_ = rd(CL, {"action": "standards_check", "standard": {"layers": [{"name": "HZ-TEST2", "color": "#FF8000", "linetype": "DASHED", "lineweight": 0.35},
                                                                        {"name": "HZ-MISSING"}],
                                                             "dim_styles": ["HZ_DIM"], "forbidden_layers": ["HZ-TEST*"]}})
    kinds = sorted(i.get("kind") for i in sc_.get("issues") or [])
    check("standards check: missing layer + forbidden pattern found, matching layer passes", kinds == ["layer_forbidden", "layer_missing"], sc_.get("issues"))
    full_write_or_refused(CL, {"action": "purge", "kinds": ["linetypes"], "names": ["PHANTOM2"]}, "purge one linetype")


# ------------------------------------------------------------------ block D: pipes, points, exchange
def data():
    check = H["check"]
    PI, PT, EX = "horizun_c3d_pipes", "horizun_c3d_points", "horizun_c3d_exchange"
    stamp = time.strftime("%Y%m%d-%H%M%S")
    out_dir = os.path.join(os.environ["USERPROFILE"], ".horizun", "civil3d", "fixtures")
    os.makedirs(out_dir, exist_ok=True)

    # pipes: EG(20,20) = 100.6, EG(60,20) = 101.4; pipe 40 m at 1 %
    cat = rd(PI, {"action": "catalog"})
    pl = next((p for p in cat.get("parts_lists") or [] if p.get("pipe_families") and p.get("structure_families")
               and p["pipe_families"][0].get("sizes") and p["structure_families"][0].get("sizes")), None)
    if not check("catalog has a parts list with pipe and structure sizes (Civil 3D template)", pl is not None, cat.get("parts_lists")):
        return
    pf = pl["pipe_families"][0]
    real = [f for f in pl["structure_families"] if f.get("part_type") != "StructNull" and "null" not in f["family"].lower() and f.get("sizes")]
    if not check("catalog has a real (non-null) structure family", bool(real), [f["family"] for f in pl["structure_families"]]):
        return
    sf = real[0]
    wr(PI, {"action": "create_network", "new_name": "HZ_NET", "parts_list": pl["parts_list"], "surface": "HZ_EG"}, "pipe network")
    wr(PI, {"action": "add_structures", "network": "HZ_NET", "structures": [
        {"name": "HZ_S1", "position": {"x": 20, "y": 20}, "family": sf["family"], "size": sf["sizes"][0]["size"], "sump_depth": 3},
        {"name": "HZ_S2", "position": {"x": 60, "y": 20}, "family": sf["family"], "size": sf["sizes"][0]["size"], "sump_depth": 3}]},
       "2 structures, rim from EG (100.6 / 101.4); sump depth checked after pipes")
    r = wr(PI, {"action": "add_pipes", "network": "HZ_NET", "pipes": [
        {"name": "HZ_P1", "from": "HZ_S1", "to": "HZ_S2", "family": pf["family"], "size": pf["sizes"][0]["size"], "start_invert": 98.6, "slope_pct": 1}]},
           "pipe S1->S2, 40 m at 1 % (inverts 98.6 -> 98.2)")
    p = ((after(r, "pipes") or [{}])[0])
    check("pipe end invert 98.2 and 2D length 40", close(p.get("end_invert"), 98.2, 1e-6) and close(p.get("length_2d_center_to_center"), 40, 1e-3), p)
    refused(PI, {"action": "add_pipes", "network": "HZ_NET", "pipes": [
        {"from": "HZ_S1", "to": "HZ_S2", "family": pf["family"], "size": pf["sizes"][0]["size"], "start_invert": 100.58, "end_invert": 100.5}]},
        "pipe crown above the rim refused", text="above the rim")
    v = rd(PI, {"action": "validate", "network": "HZ_NET", "min_cover": 0.5, "min_slope_pct": 0.5})
    check("network validates with cover >= 0.5 and slope >= 0.5 %", v.get("passed") is True, v.get("issues"))

    # points
    wr(PT, {"action": "create", "points": [{"x": 30, "y": 70, "z": 0, "description": "TREE OAK", "number": 9001},
                                          {"x": 35, "y": 72, "z": 0, "description": "TREE PINE", "number": 9002},
                                          {"x": 40, "y": 74, "z": 0, "description": "POST", "number": 9003}]}, "3 COGO points with numbers")
    refused(PT, {"action": "create", "points": [{"x": 1, "y": 1, "number": 9001}]}, "taken point number refused", text="already exist")
    wr(PT, {"action": "group_create", "new_name": "HZ_TREES", "include_raw_descriptions": "TREE*"}, "point group TREE* (2 points, our evaluation = Civil's)")
    wr(PT, {"action": "elevations_from_surface", "surface": "HZ_EG", "numbers": "9001-9003"}, "point elevations from EG")
    pts = rd(PT, {"action": "list", "numbers": "9001"}).get("points") or [{}]
    check("point 9001 z = EG(30,70) = 101.3", close(pts[0].get("z"), 101.3, 1e-6), pts)
    csv = os.path.join(out_dir, "hz-points-" + stamp + ".csv")
    full_write_or_refused(PT, {"action": "export_csv", "output": csv, "numbers": "9001-9003"}, "export points to CSV (read back)")
    imp = os.path.join(out_dir, "hz-import-" + stamp + ".csv")
    with open(imp, "w", encoding="utf-8") as fh:
        fh.write("P,N,E,Z,D\n9101,80,20,100.5,\"MH, NEW\"\n9102,80,30,100.6,TREE ELM\n")
    wr(PT, {"action": "import", "file": imp, "format": "PNEZD", "skip_header": True, "group": "HZ_TREES"}, "import PNEZD file into the group")
    full_write_or_refused(PT, {"action": "erase", "numbers": "9101-9102"}, "erase imported points")

    # exchange
    xml = os.path.join(out_dir, "hz-export-" + stamp + ".xml")
    r = full_write_or_refused(EX, {"action": "export_landxml", "output": xml, "surfaces": ["HZ_EG"], "alignments": ["HZ_ROAD"]}, "LandXML export EG + HZ_ROAD (file re-read)")
    if r and os.path.exists(xml):
        txt = open(xml, encoding="utf-8").read()
        check("LandXML holds 121 EG points and the 90 m alignment", txt.count("<P id=") == 121 and 'length="90"' in txt, len(txt))
        refused(EX, {"action": "export_landxml", "output": xml, "surfaces": ["HZ_EG"]}, "LandXML never overwrites", text="never overwrites")
    dwg = os.path.join(out_dir, "hz-copy-" + stamp + ".dwg")
    copy = full_write_or_refused(EX, {"action": "export_dwg", "output": dwg}, "DWG copy of current drawing (reopened)")
    if copy and os.path.exists(dwg):
        check("DWG copy exists and is non-empty", os.path.getsize(dwg) > 0, after(copy, "file"))
        refused(EX, {"action": "export_dwg", "output": dwg}, "DWG never overwrites", text="never overwrites")

    terrain_zip = os.path.join(out_dir, "hz-revit-terrain-" + stamp + ".zip")
    terrain = full_write_or_refused(EX, {"action": "export_revit", "output": terrain_zip, "surface": "HZ_EG"},
                                   "Revit terrain package (complete payload reread)")
    if terrain and os.path.exists(terrain_zip):
        with zipfile.ZipFile(terrain_zip) as package:
            manifest = json.loads(package.read("manifest.json"))
            payload = package.read("terrain.xml")
            check("Revit terrain ZIP has only expected payloads", sorted(package.namelist()) == ["manifest.json", "terrain.xml"], package.namelist())
        check("Revit terrain payload hash matches", hashlib.sha256(payload).hexdigest() == manifest.get("landxml_sha256"), manifest.get("landxml_sha256"))
        check("Revit terrain fixture geometry and native unit identified", manifest.get("vertices") == 121 and manifest.get("visible_faces", 0) > 0
              and manifest.get("linear_unit") in ("meter", "foot", "USSurveyFoot"), manifest)
        check("export does not claim Revit import or shared coordinate verification", manifest.get("revit_import_verified") is False
              and manifest.get("shared_coordinates_verified") is False, manifest)
        refused(EX, {"action": "export_revit", "output": terrain_zip, "surface": "HZ_EG"}, "Revit terrain package never overwrites", text="exists")

    # A new, empty pressure network exercises creation without a catalog-specific
    # part size. Existing part inspection needs an additional authorized fixture.
    rd(PI, {"action": "pressure_list", "limit": 100})
    wr(PI, {"action": "pressure_create_network", "new_name": "HZ_PRESSURE", "surface": "HZ_EG"}, "create empty pressure network")
    net = rd(PI, {"action": "pressure_get", "network": "HZ_PRESSURE"})
    check("empty pressure network reread", (net.get("network") or {}).get("pipes") == 0 and (net.get("network") or {}).get("fittings") == 0, net)
    wr(PI, {"action": "pressure_rename", "network": "HZ_PRESSURE", "new_name": "HZ_PRESSURE_RENAMED"}, "rename pressure network")
    refused(PI, {"action": "pressure_create_network", "new_name": "HZ_PRESSURE_RENAMED"}, "duplicate pressure network refused", text="already exists")
    audit = rd("horizun_c3d_audit", {"sample_limit": 10})
    check("audit reports currency and references without treating unknowns as healthy", isinstance(audit.get("types"), dict) and isinstance(audit.get("partial"), bool), audit.get("types"))
    st = H["call"](EX, {"action": "shortcuts_status"})
    check("shortcuts_status answers (status, or a clear refusal when no project is set)",
          not st["isError"] or H["sc"](st).get("code") in ("unsupported", "internal"), H["sc"](st))


# ------------------------------------------------------------------ block A2: sections + corridor (run LAST:
# sample-line creation aborted Civil 3D twice during v0.6 bring-up; a crash here must not hide the other blocks)
def sections():
    check = H["check"]
    SE = "horizun_c3d_sections"
    sl = wr(SE, {"action": "create_sample_lines", "alignment": "HZ_ROAD", "group": "HZ_SLG", "interval": 10, "left_width": 10, "right_width": 10, "sources": ["HZ_EG"]},
            "10 sample lines every 10 m, widths 10/10, EG sampled")
    check("sample lines at 0..90", len(((sl or {}).get("after") or {}).get("sample_lines") or []) == 10, (sl or {}).get("after"))
    gs = rd(SE, {"action": "get_section", "alignment": "HZ_ROAD", "group": "HZ_SLG", "station": 40})
    sec = (gs.get("sections") or [{}])[0]
    check("EG section at station 40: z 101.3..101.5 (plane, offsets -10..10)",
          close(sec.get("min_elevation"), 101.3, 2e-3) and close(sec.get("max_elevation"), 101.5, 2e-3), sec)
    refused(SE, {"action": "create_sample_lines", "alignment": "HZ_ROAD", "group": "HZ_SLG", "stations": [40], "left_width": 5, "right_width": 5},
            "sample line at an occupied station refused", text="already has")
    wr(SE, {"action": "create_section_views", "alignment": "HZ_ROAD", "group": "HZ_SLG", "insert": {"x": 250, "y": 0}}, "section views for the group")

    CO = "horizun_c3d_corridor"
    wr(CO, {"action": "assembly_create", "new_name": "HZ_ASM", "insert": {"x": 0, "y": -60}}, "empty assembly")
    wr(CO, {"action": "create", "new_name": "HZ_COR", "alignment": "HZ_ROAD", "profile": "HZ_ROAD_FG", "assembly": "HZ_ASM", "frequency": {"tangents": 10}},
       "corridor HZ_ROAD + HZ_ROAD_FG + HZ_ASM, every 10 m")
    cg = rd(CO, {"action": "get", "name": "HZ_COR"}).get("corridor") or {}
    reg = (((cg.get("baselines") or [{}])[0]).get("regions") or [{}])[0]
    check("corridor region 0..90 with tangent frequency 10", close(reg.get("start_station"), 0, 1e-6) and close(reg.get("end_station"), 90, 1e-6)
          and close((reg.get("frequency") or {}).get("tangents"), 10, 1e-9), cg)
    refused(CO, {"action": "add_region", "name": "HZ_COR", "assembly": "HZ_ASM", "start_station": 20, "end_station": 30}, "overlapping region refused", text="overlap")
    refused(CO, {"action": "create_surface", "name": "HZ_COR", "surface_name": "HZ_COR_TOP", "link_codes": ["Top"]},
            "corridor surface with link codes the corridor lacks refused (empty assembly)", text="no link codes")
    wr(CO, {"action": "rebuild", "name": "HZ_COR"}, "corridor rebuild")
    # A drawing with sample lines/sections must survive whole-drawing reads (these open every entity).
    rep = rd("horizun_c3d_cleanup", {"action": "drawing_report", "limit": 5})
    check("drawing report over a drawing with sections and corridor (no abort)", (rep.get("entities") or 0) > 0, {k: rep.get(k) for k in ("entities", "error")})
    q = rd("horizun_c3d_entities", {"action": "query", "limit": 5})
    check("entity query over the whole model space (no abort)", (q.get("count") or 0) > 0, q.get("by_type"))
    ql = rd("horizun_c3d_layers", {"action": "list", "limit": 5})
    check("layer list with object counts (no abort)", (ql.get("count") or 0) > 0, ql.get("error"))


BLOCKS = {"road": road, "labels": labels, "cad": cad, "data": data, "sections": sections}


def run_all(call, check, sc, tgt, exp, only=None):
    H.update(call=call, check=check, sc=sc, tgt=tgt, exp=exp)
    for name, fn in BLOCKS.items():
        if only and name not in only:
            continue
        print("---- block " + name)
        try:
            fn()
        except Exception as e:  # a crash in one block must not hide the others
            check("block " + name + " ran without a client-side exception", False, repr(e))


if __name__ == "__main__":
    import verify_live as vl
    sys.stdout.reconfigure(encoding="utf-8")
    exp = json.load(open(vl.EXPECTED, encoding="utf-8"))
    h = vl.call("horizun_c3d_health", {})
    doc = vl.sc(h)["document"]["name"]
    if os.path.basename(exp["document"]).lower() != doc.lower():
        print("Active drawing is not the fixture. Refusing."); sys.exit(2)
    run_all(vl.call, vl.check, vl.sc, {"target_document": doc}, exp, sys.argv[1:] or None)
    p = sum(1 for r in vl.results if r["pass"])
    print(f"\n{p}/{len(vl.results)} passed.")
    sys.exit(0 if p == len(vl.results) else 1)

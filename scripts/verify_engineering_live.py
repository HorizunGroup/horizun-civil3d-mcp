"""Typed engineering acceptance on an explicitly selected disposable drawing.

No screen, keyboard, C# execution, saves or source-DWG edits. Default mode is
read-only preflight. --execute authorizes new fixture objects in --targetdoc.
Example: python scripts/verify_engineering_live.py --pid 123 --targetdoc Drawing1.dwg
         --output C:/Temp/fresh-engineering-report --execute
"""
import argparse
import datetime
import json
import math
import os
import re
import time
from pathlib import Path
import uuid

from mcp_call import Session
import mcp_call

CO = "horizun_c3d_corridor"
LY = "horizun_c3d_layouts"


def content(result):
    return (result or {}).get("structuredContent") or {}


class Acceptance:
    def __init__(self, session, targetdoc):
        self.session, self.targetdoc = session, targetdoc
        self.checks, self.calls, self.limits = [], [], []

    def check(self, label, passed, detail=None):
        self.checks.append({"label": label, "pass": bool(passed), "detail": detail})
        print(("PASS " if passed else "FAIL ") + label, flush=True)
        if not passed:
            raise RuntimeError(label + ": " + json.dumps(detail, ensure_ascii=False))

    def call(self, tool, args):
        request = dict(args, target_document=self.targetdoc)
        result = self.session.call(tool, request)
        self.calls.append({"tool": tool, "arguments": request, "result": result})
        return result

    def read(self, tool, args):
        result = self.call(tool, args)
        self.check(tool + ":" + args.get("action", "read") + " read succeeds", not result.get("isError"), content(result))
        return content(result)

    def write(self, tool, args):
        # Never reapply an unknown/committed outcome or automatically retry tokens.
        dry = self.call(tool, dict(args, dry_run=True))
        data = content(dry)
        self.check(tool + ":" + args["action"] + " rehearsal changes nothing",
                   not dry.get("isError") and data.get("committed") is False and bool(data.get("confirmation_token")), data)
        applied = self.call(tool, dict(args, dry_run=False, confirmation_token=data["confirmation_token"]))
        failed = content(applied)
        if applied.get("isError") and failed.get("code") == "confirmation_refused" and failed.get("confirmation_state") == "stale_plan" and failed.get("committed") is not True:
            # This explicit pre-transaction refusal is not an unknown write outcome.
            # Refresh the whole plan once, never reuse the rejected token.
            time.sleep(1)
            dry = self.call(tool, dict(args, dry_run=True)); data = content(dry)
            self.check(tool + ":" + args["action"] + " stale plan refreshed without changes",
                       not dry.get("isError") and data.get("committed") is False and bool(data.get("confirmation_token")), data)
            applied = self.call(tool, dict(args, dry_run=False, confirmation_token=data["confirmation_token"]))
        after = content(applied)
        self.check(tool + ":" + args["action"] + " new transaction verifies match",
                   not applied.get("isError") and after.get("committed") is True and
                   (after.get("verified") or {}).get("status") == "match", after)
        return after

    def refused(self, tool, args, code):
        result = self.call(tool, args)
        self.check(tool + ":" + args["action"] + " refuses " + code,
                   result.get("isError") and content(result).get("code") == code, content(result))


def fixture(a, source_dwg, source_assembly, prefix):
    """Create own flat terrain, straight alignment and constant design profile."""
    names = {k: prefix + "_" + k for k in ("EG", "AL", "FG", "ASM", "COR", "SHEET")}
    styles = a.read("horizun_c3d_styles", {"action": "list", "object_type": "surface"})["styles"]
    a.check("fixture drawing has an exact surface style", bool(styles), styles)
    style = next((s["name"] for s in styles if s["name"] == "Contours and Triangles"), styles[0]["name"])
    a.write("horizun_c3d_surface", {"action": "create_tin", "new_name": names["EG"], "style": style})
    a.write("horizun_c3d_surface", {"action": "add_data", "name": names["EG"],
            "vertices": [{"x": x, "y": y, "z": 100} for x, y in ((-100, -100), (200, -100), (200, 200), (-100, 200))]})
    a.write("horizun_c3d_alignment", {"action": "create_by_pis", "new_name": names["AL"],
            "pis": [{"x": 5, "y": 50}, {"x": 95, "y": 50}]})
    a.write("horizun_c3d_profile", {"action": "create_layout", "alignment": names["AL"], "new_name": names["FG"],
            "pvis": [{"station": 0, "elevation": 101}, {"station": 90, "elevation": 101}]})
    a.write(CO, {"action": "assembly_import", "source_dwg": source_dwg, "source_assembly": source_assembly,
            "new_name": names["ASM"], "insert": {"x": 0, "y": -60}})
    # Set targets before first rebuild, avoiding missing daylight target geometry.
    a.write(CO, {"action": "create", "new_name": names["COR"], "alignment": names["AL"], "profile": names["FG"],
            "assembly": names["ASM"], "start_station": 0, "end_station": 90,
            "frequency": {"tangents": 10, "curves": 10, "spirals": 10, "profile_curves": 10}, "rebuild": False})
    return names


def quantity_check(a, quantity):
    stations = quantity.get("stations") or []
    a.check("quantities have multiple applied stations", len(stations) >= 2, stations)
    for report in quantity.get("quantities") or []:
        code = report["shape_code"]
        samples = [(row["station"], row["areas_by_shape_code"].get(code)) for row in stations]
        covered = sum((s1 - s0) * (ar0 / 2 + ar1 / 2) for (s0, ar0), (s1, ar1) in zip(samples, samples[1:]) if ar0 is not None and ar1 is not None)
        actual = report.get("covered_volume_estimate")
        if actual is not None:
            a.check("independent end-area integration " + code, math.isclose(actual, covered, rel_tol=1e-9, abs_tol=1e-8), report)
        a.check("quantity declares estimated method " + code, report.get("method") == "average_end_area_estimate", report)
        if not report.get("complete"):
            a.check("incomplete code volume is null " + code, report.get("estimated_volume") is None, report)


def engineering(a, names):
    selector = {"name": names["COR"]}
    targets = a.read(CO, dict(selector, action="get_targets"))["targets"]
    a.check("real imported assembly exposes targets", bool(targets), targets)
    surface = a.read("horizun_c3d_surface", {"action": "get", "name": names["EG"]})["surfaces"][0]
    updates = [{"target_index": t["target_index"], "handles": [surface["handle"]]} for t in targets if t["target_type"] == "Surface"]
    a.check("assembly has a surface target", bool(updates), targets)
    a.write(CO, dict(selector, action="set_targets", targets=updates))
    reread = a.read(CO, dict(selector, action="get_targets"))["targets"]
    for update in updates:
        a.check("surface target reread " + str(update["target_index"]), reread[update["target_index"]]["handles"] == update["handles"], reread)
    alignment = a.read("horizun_c3d_alignment", {"action": "get", "name": names["AL"]})["alignment"]
    a.refused(CO, dict(selector, action="set_targets", targets=[{"target_index": updates[0]["target_index"], "handles": [alignment["handle"]]}]), "unsupported")
    original = a.read(CO, dict(selector, action="get"))["corridor"]
    codes = original.get("shape_codes") or []
    a.check("real imported assembly provides shape codes", bool(codes), original)
    geometry = a.read(CO, dict(selector, action="applied_geometry", shape_codes=codes))
    a.check("applied geometry has shape links and XYZ points", any(shape.get("links") for row in geometry.get("stations", []) for shape in row.get("shapes", [])), geometry)
    quantity = a.read(CO, dict(selector, action="region_quantities", shape_codes=codes,
                            material_map={code: "fixture_" + code for code in codes}))
    quantity_check(a, quantity)
    a.refused(CO, dict(selector, action="region_quantities", shape_codes=["HZ_NONEXISTENT_SHAPE"]), "not_found")
    a.refused(CO, dict(selector, action="split_region", split_station=0, new_region_name=names["COR"] + "_INVALID"), "invalid_input")
    a.write(CO, dict(selector, action="split_region", split_station=45, new_region_name=names["COR"] + "_RIGHT"))
    split = a.read(CO, dict(selector, action="get"))["corridor"]
    regions = split["baselines"][0]["regions"]
    a.check("split creates adjacent 0..45 and 45..90 regions", len(regions) == 2 and
            regions[0]["start_station"] == 0 and regions[0]["end_station"] == 45 and
            regions[1]["start_station"] == 45 and regions[1]["end_station"] == 90, regions)
    right = a.read(CO, dict(selector, action="get_targets", region_index=1))["targets"]
    a.check("split retains exact target definitions on right", right == reread, right)
    a.write(CO, dict(selector, action="merge_regions", last_region_index=1))
    merged = a.read(CO, dict(selector, action="get"))["corridor"]
    a.check("merge restores original readable baseline definitions", merged["baselines"] == original["baselines"], merged)
    after_quantity = a.read(CO, dict(selector, action="region_quantities", shape_codes=codes))
    a.check("split-merge preserves areas at every applied station", after_quantity["stations"] == quantity["stations"], after_quantity)
    return selector


def sheets(a, names, resume=False):
    if resume:
        layouts = a.read(LY, {"action": "list"})["layouts"]
        existing = next((row for row in layouts if row["name"] == names["SHEET"]), None)
        if existing:
            a.check("resumed sheet has no previously committed viewport", not existing["viewports"], existing)
        else:
            a.write(LY, {"action": "create", "new_name": names["SHEET"]})
    else:
        a.write(LY, {"action": "create", "new_name": names["SHEET"]})
    args = {"action": "alignment_viewport", "layout": names["SHEET"], "alignment": names["AL"],
            "station": 45, "center": {"x": 100, "y": 100}, "width": 180, "height": 90, "scale": 1}
    created = a.write(LY, args)
    vp = created["after"]["viewport"]
    target = created["after"]["view_target_wcs"]
    a.check("straight alignment viewport camera target at (50,50)", target.get("x") == 50 and target.get("y") == 50, created)
    refreshed = a.write(LY, {"action": "refresh_alignment_viewport", "handle": vp["handle"]})
    a.check("explicit refresh retains camera and scale", refreshed["after"]["view_target_wcs"] == target and refreshed["after"]["viewport"]["scale"] == vp["scale"], refreshed)
    rows = a.read(LY, {"action": "list"})["layouts"]
    persisted = next(l for l in rows if l["name"] == names["SHEET"])
    a.check("layout reread includes persistent locked viewport", any(v["handle"] == vp["handle"] and v["locked"] for v in persisted["viewports"]), persisted)
    a.refused(LY, dict(args, station=100), "invalid_input")


def undo_created_fixture(a, prefix):
    styles = a.read("horizun_c3d_styles", {"action": "list", "object_type": "surface"})["styles"]
    name = prefix + "_UNDO_CREATED"
    a.write("horizun_c3d_surface", {"action": "create_tin", "new_name": name, "style": styles[0]["name"]})
    for options in ({"dry_run": True}, {"dry_run": False, "confirmation_token": "invalid-fixture-confirmation"}):
        result = a.call("horizun_c3d_document", dict(action="undo_last", **options))
        data = content(result)
        a.check("automatic UNDO refused before execution", result.get("isError") and
                data.get("code") == "unsupported" and data.get("committed") is False, data)
    a.read("horizun_c3d_surface", {"action": "get", "name": name})
    a.limits.append("Automatic undo_last is disabled in v0.9.2; fixture creation remains intact after refusal. Native manual UNDO is outside this test.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--server", help="Explicit test server executable; default is the installed canonical server")
    parser.add_argument("--targetdoc", required=True)
    parser.add_argument("--output", required=True, help="Fresh absolute folder for the complete JSON evidence")
    parser.add_argument("--execute", action="store_true", help="Create fixture objects in the explicitly named disposable drawing")
    parser.add_argument("--resume-prefix", help="Resume an existing HZ_ENG_<8 hex digits> fixture after an explicitly reviewed stop")
    parser.add_argument("--phase", choices=("all", "sheets", "undo"), default="all", help="Later phases require an explicitly reviewed resume prefix")
    parser.add_argument("--source-dwg", default=str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Autodesk/AutoCAD 2025/C3D/Help/Civil Tutorials/Drawings/Corridor-1a.dwg"))
    parser.add_argument("--source-assembly", default="Primary Road Full Section")
    opts = parser.parse_args(argv)
    if opts.server:
        mcp_call.EXE = str(Path(opts.server).resolve(strict=True))
    if opts.resume_prefix and (not opts.execute or not re.fullmatch(r"HZ_ENG_[0-9A-F]{8}", opts.resume_prefix)):
        parser.error("--resume-prefix requires --execute and an exact generated fixture prefix")
    if opts.phase != "all" and not opts.resume_prefix: parser.error("Later phases require --resume-prefix")
    out = Path(opts.output)
    if not out.is_absolute() or out.exists(): parser.error("--output must be a fresh absolute folder")
    if not Path(opts.source_dwg).is_absolute() or not Path(opts.source_dwg).is_file(): parser.error("--source-dwg must exist at an absolute path")
    out.mkdir(parents=True, exist_ok=False)
    session = Session(); a = Acceptance(session, opts.targetdoc)
    report = {"mode": "execute" if opts.execute else "read_only_preflight", "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "pid": opts.pid, "targetdoc": opts.targetdoc, "checks": a.checks, "calls": a.calls, "limits": a.limits}
    try:
        selected = session.call("horizun_c3d_target", {"pid": opts.pid})
        report["selection"] = selected
        a.check("requested PID selected with matching contract", not selected.get("isError") and
                (content(selected).get("will_talk_to") or {}).get("pid") == opts.pid and
                any(t.get("pid") == opts.pid and t.get("contract_match") for t in content(selected).get("targets", [])), content(selected))
        health = session.call("horizun_c3d_health", {})
        report["health"] = health
        a.check("health answers selected bridge", not health.get("isError"), content(health))
        a.read("horizun_c3d_document", {"action": "info"})
        a.read(CO, {"action": "assembly_list"})
        if opts.execute:
            prefix = opts.resume_prefix or "HZ_ENG_" + uuid.uuid4().hex[:8].upper(); report["fixture_prefix"] = prefix
            names = {k: prefix + "_" + k for k in ("EG", "AL", "FG", "ASM", "COR", "SHEET")} if opts.resume_prefix else fixture(a, opts.source_dwg, opts.source_assembly, prefix)
            report["names"] = names
            if opts.phase == "all": engineering(a, names)
            if opts.phase in ("all", "sheets"): sheets(a, names, resume=bool(opts.resume_prefix))
            undo_created_fixture(a, prefix)
        else:
            report["next"] = "Repeat with --execute after exclusive access to the explicitly named disposable drawing is authorized."
        report["status"] = "passed"
    except Exception as exc:
        report["status"] = "failed"; report["error"] = str(exc)
        print("STOP: " + str(exc), flush=True)
    finally:
        session.close()
        (out / "engineering-evidence.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(str(out / "engineering-evidence.json"), flush=True)
    return 0 if report.get("status") == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())

"""Acceptance on an explicitly targeted generated drawing; all outputs must be new.

The caller authorizes --full-write for typed file exports only. The original
settings bytes are restored even on failure; arbitrary C# is never enabled.
"""
import argparse, csv, hashlib, io, json, math, os, pathlib, sys, zipfile
from mcp_call import Session

parser = argparse.ArgumentParser()
parser.add_argument('--pid', type=int, required=True)
parser.add_argument('--document', required=True)
parser.add_argument('--output', type=pathlib.Path, required=True)
parser.add_argument('--full-write', action='store_true', required=True)
args = parser.parse_args()
out = args.output.resolve()
assert out.is_dir() and not (out / 'interop-results.json').exists()
settings = pathlib.Path(os.environ['USERPROFILE']) / '.horizun/civil3d/settings.json'
original = settings.read_bytes() if settings.exists() else None
records, checks = [], []
s = Session()
def check(name, passed, detail=None):
    checks.append({'name': name, 'passed': bool(passed), 'detail': detail})
    print(('PASS ' if passed else 'FAIL ') + name, flush=True)
    if not passed: raise AssertionError(name + ': ' + str(detail))
def call(tool, data, failure=False):
    result = s.call(tool, data)
    records.append({'tool': tool, 'arguments': data, 'result': result})
    if not failure and result.get('isError'): raise RuntimeError(json.dumps(result))
    return result.get('structuredContent', {})
def write(tool, data):
    data = {'target_document': args.document, **data}
    dry = call(tool, {**data, 'dry_run': True})
    check(data['action'] + ' rehearsal writes nothing', dry.get('committed') is False and bool(dry.get('confirmation_token')))
    applied = call(tool, {**data, 'dry_run': False, 'confirmation_token': dry['confirmation_token']})
    verified = applied.get('verified', {})
    check(data['action'] + ' postcommit reread', verified.get('status') == 'match' and verified.get('checks_failed') == 0, verified)
    return applied
try:
    call('horizun_c3d_target', {'pid': args.pid})
    health = call('horizun_c3d_health', {})
    check('explicit new-drawing target', health['document']['name'] == args.document and not health['document']['ever_saved'])
    check('C# channel initially disabled', health['bridge']['permissions']['enable_execute_csharp'] is False)
    selected = call('horizun_c3d_points', {'action': 'list', 'numbers': '9001-9002'})
    check('test point numbers unused', selected.get('count', len(selected.get('points', []))) == 0, selected)
    # This explicit command-line flag represents the owner's test authorization.
    test_settings = json.loads(original) if original else {}
    test_settings.update(permission_profile='full_write', enable_execute_csharp=False)
    settings.write_text(json.dumps(test_settings), encoding='utf-8')
    points = [{'number': 9001, 'x': 2, 'y': 2, 'z': 100.4, 'description': 'fixture,A'},
              {'number': 9002, 'x': 8, 'y': 8, 'z': 101.6, 'description': 'fixture B'}]
    write('horizun_c3d_points', {'action': 'create', 'points': points})
    output_csv = out / 'points-editable.csv'
    export = write('horizun_c3d_points', {'action': 'export_editable_csv', 'numbers': '9001-9002', 'output': str(output_csv)})
    rows = list(csv.DictReader(io.StringIO(output_csv.read_text(encoding='utf-8-sig'))))
    check('CSV exported all point identities', {r['number'] for r in rows} == {'9001', '9002'})
    rows[0]['easting'] = '2.25'; rows[0]['northing'] = '2.5'; rows[0]['elevation'] = '100.45'
    rows[0]['description'] = 'edited, "quoted" fixture'
    edited = out / 'points-edited.csv'
    def emit(path, data):
        with path.open('w', encoding='utf-8', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=list(data[0])); writer.writeheader(); writer.writerows(data)
    emit(edited, rows)
    apply_args = {'action': 'apply_csv', 'numbers': '9001-9002', 'file': str(edited), 'target_document': args.document}
    dry = call('horizun_c3d_points', {**apply_args, 'dry_run': True})
    rows[0]['elevation'] = '100.46'; emit(edited, rows)
    refused = call('horizun_c3d_points', {**apply_args, 'dry_run': False, 'confirmation_token': dry['confirmation_token']}, failure=True)
    check('CSV altered after rehearsal refused', refused.get('confirmation_state') == 'stale_plan', refused)
    write('horizun_c3d_points', apply_args)
    reread = call('horizun_c3d_points', {'action': 'list', 'numbers': '9001-9002'})
    check('independent point reread returned two points', len(reread.get('points', [])) == 2, reread)
    refused = call('horizun_c3d_points', {**apply_args, 'dry_run': True}, failure=True)
    check('changed-source CSV refused', refused.get('code') == 'invalid_input', refused)
    package = out / 'civil-terrain.zip'
    write('horizun_c3d_exchange', {'action': 'export_revit', 'surface': 'Fixture_Terrain', 'output': str(package)})
    with zipfile.ZipFile(package) as archive:
        check('terrain archive has exact three payloads', set(archive.namelist()) == {'terrain.xml', 'terrain.obj', 'manifest.json'})
        obj = archive.read('terrain.obj').decode('utf-8')
        vertices = [tuple(map(float, line.split()[1:])) for line in obj.splitlines() if line.startswith('v ')]
        faces = [line for line in obj.splitlines() if line.startswith('f ')]
        check('actual Civil TIN exported without thinning', len(vertices) == 4 and len(faces) == 2)
        check('exported source plane independently measured', all(math.isclose(z, 100 + .1*x + .1*y, abs_tol=1e-10) for x,y,z in vertices))
    refused = call('horizun_c3d_exchange', {'action': 'export_revit', 'surface': 'Fixture_Terrain', 'output': str(package), 'target_document': args.document}, failure=True)
    check('existing export destination refused', refused.get('code') == 'invalid_input', refused)
    write('horizun_c3d_exchange', {'action': 'export_dwg', 'output': str(out / 'CivilTerrainFixture.dwg')})
    report = call('horizun_c3d_surface', {'action': 'compare_design', 'design': 'Fixture_Terrain', 'built': 'Fixture_Built',
        'tolerance': .01, 'points': [{'x': 5, 'y': 5}, {'x': 20, 'y': 20}]})['comparison']
    check('missing domain and tolerance failure reported', report['valid_samples'] == 1 and report['missing_samples'] == 1
        and report['failed_samples'] == 1 and report['sample_coverage'] == .5, report)
finally:
    if original is None:
        if settings.exists(): settings.unlink()
    else: settings.write_bytes(original)
    try:
        health = call('horizun_c3d_health', {})
        checks.append({'name': 'C# remains disabled after restoration', 'passed': health['bridge']['permissions']['enable_execute_csharp'] is False})
    finally:
        (out / 'interop-results.json').write_text(json.dumps({'checks': checks, 'calls': records}, indent=2), encoding='utf-8')
        s.close()
print('PASS: Civil CSV and native terrain export acceptance complete.')

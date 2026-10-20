"""Verify automatic UNDO refusal on an explicitly owned fixture, without screen control."""
import argparse
import json
from pathlib import Path
import mcp_call


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--pid', required=True, type=int)
    parser.add_argument('--output', required=True)
    parser.add_argument('--server')
    parser.add_argument('--execute', action='store_true')
    args = parser.parse_args()
    if not args.execute:
        parser.error('--execute is required to create a disposable line')
    if args.server:
        mcp_call.EXE = str(Path(args.server).resolve(strict=True))
    records = []
    session = mcp_call.Session()

    def call(tool, request, error=False):
        result = session.call(tool, request)
        records.append(dict(tool=tool, arguments=request, result=result))
        assert bool(result.get('isError')) == error, result
        return result['structuredContent']

    try:
        call('horizun_c3d_target', dict(pid=args.pid))
        health = call('horizun_c3d_health', {})
        target = health['document']['name']
        assert Path(target).name == 'PublicReadinessFixture092.dwg', target
        assert health['bridge']['permissions']['enable_execute_csharp'] is False
        request = dict(action='draw', target_document=target, items=[
            dict(type='line', **{'from':dict(x=1,y=2,z=0), 'to':dict(x=11,y=2,z=0)}, color=2)])
        dry = call('horizun_c3d_entities', dict(request, dry_run=True))
        applied = call('horizun_c3d_entities', dict(request, dry_run=False,
                       confirmation_token=dry['confirmation_token']))
        assert applied['committed'] is True and applied['verified']['status'] == 'match'
        assert applied['undo']['available'] is False
        before = call('horizun_c3d_entities', dict(action='query', types=['LINE'], target_document=target))
        for request in (
            dict(action='undo_last', dry_run=True),
            dict(action='undo_last', dry_run=False, confirmation_token='invalid-fixture-confirmation'),
            dict(action='undo_last'),
        ):
            refused = call('horizun_c3d_document', dict(request, target_document=target), True)
            assert refused['code'] == 'unsupported' and refused['committed'] is False, refused
            assert refused['evidence_status'] == 'refused_before_native_undo'
            after = call('horizun_c3d_entities', dict(action='query', types=['LINE'], target_document=target))
            assert before['entities'] == after['entities']
        print('PASS: typed write verified; all three automatic UNDO variants refused before execution; independent entity reads unchanged.')
    finally:
        Path(args.output).parent.mkdir(parents=True, exist_ok=True)
        Path(args.output).write_text(json.dumps(records, indent=2), encoding='utf-8')
        session.close()


if __name__ == '__main__':
    main()

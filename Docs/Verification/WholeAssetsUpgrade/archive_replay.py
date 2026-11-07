from pathlib import Path
import gzip
import hashlib
import json
import shutil

repo = Path('/workspace/UnityBox')
base = Path('/workspace/unitybox-setup/unity')
here = Path(__file__).resolve().parent
out = repo / 'Docs/Verification/WholeAssetsUpgrade'
out.mkdir(exist_ok=True)
records = []
for scope in ['all', 'acc', 'ass', 'ao', 'viseme']:
    run_file = here / scope / 'run.json'
    if not run_file.is_file():
        continue
    run = json.loads(run_file.read_text())
    if not run['passed']:
        continue
    project = base / 'projects' / ('whole-assets-replay-' + scope + '-20261108')
    native = project / '.unitybox-component-upgrade'
    result = json.loads((native / 'migrated.json').read_text())
    assert result == run['result'] and result['passed'] and run['exit_code'] == 0
    provenance = json.loads((here / scope / 'input-provenance.json').read_text())
    site = Path(json.loads((base / 'whole-assets-vpm-local.json').read_text())['output']) / 'site'
    for package in provenance['current_zip_inputs']:
        current_zip = site / (package['package'] + '-' + package['version'] + '.zip')
        assert hashlib.sha256(current_zip.read_bytes()).hexdigest() == package['sha256']
        source = project / 'Packages' / package['package']
        assert json.loads((source / 'package.json').read_text())['version'] == package['version']
    dest = out / 'Scopes' / scope
    dest.mkdir(parents=True, exist_ok=True)
    for name in ['run.json', 'input-provenance.json']:
        shutil.copy2(here / scope / name, dest / name)
    for name in ['migrated.json', 'legacy-state.json', 'config.json']:
        shutil.copy2(native / name, dest / name)
    for owner in result['nativeOwners']:
        for source in owner['sourceFiles']:
            data = (project / source['path']).read_bytes()
            assert hashlib.sha256(data).hexdigest() == source['sha256']
            relative = Path('source-snapshots') / owner['owner'] / Path(source['path']).name
            target = dest / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        dll = Path(owner['assemblyPath'])
        assert hashlib.sha256(dll.read_bytes()).hexdigest() == owner['assemblySha256']
    for item in provenance['restored_files']:
        data = (project / item['path']).read_bytes()
        target = dest / 'post' / (item['path'] + '.gz')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(gzip.compress(data, mtime=0))
    for batch in result['batches']:
        assert batch['applyInvocations'] == 1 and batch['confirmationInvocations'] == 1 and batch['cancellationInvocations'] == 1
        backup = Path(batch['backupDirectory'])
        for relative in batch['changedAssets']:
            data = (backup / relative).read_bytes()
            before = next(value['sha256'] for value in batch['beforeFiles'] if value['path'] == relative)
            assert hashlib.sha256(data).hexdigest() == before
            for path in [backup / relative, Path(str(backup / relative) + '.meta')]:
                if path.is_file():
                    target = dest / 'backups' / batch['owner'] / (path.relative_to(backup).as_posix() + '.gz')
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(gzip.compress(path.read_bytes(), mtime=0))
        shutil.copy2(backup / 'report.json', dest / ('batch-' + batch['owner'] + '.json'))
    summary = {'scope': scope, 'passed': True, 'missing_before_upgrade': result['missingScripts'],
               'repaired_components': result['repairedComponents'], 'inspector_targets': result['inspectorTargets'],
               'batches': [{'owner': b['owner'], 'repairs': b['repairedComponents'],
                            'confirmations': b['confirmationInvocations'], 'cancellation_checks': b['cancellationInvocations']} for b in result['batches']],
               'comparison_passes': result['comparisonPasses'], 'native_result': 'migrated.json'}
    (dest / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    hashes = {p.relative_to(dest).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
              for p in dest.rglob('*') if p.is_file() and p.name != 'sha256.json'}
    (dest / 'sha256.json').write_text(json.dumps(hashes, sort_keys=True, indent=2) + '\n')
    records.append(summary)
shutil.copy2(here / 'ComponentUpgradeFixture.cs', out / 'ComponentUpgradeFixture.cs')
shutil.copy2(here / 'ComponentUpgradeReferenceHolder.cs', out / 'ComponentUpgradeReferenceHolder.cs')
for name in ['run_replay.py', 'resume_replay.py', 'archive_replay.py']:
    shutil.copy2(here / name, out / name)
summary = {'passed': len(records) == 5, 'scopes': records,
           'producer_archive': '../ComponentReferences/LegacyUpgrade',
           'producer_revision': 'e62c741f7921071834924f75cbfd80f1a50a5304',
           'scope': 'Current-only ZIP consumers replay genuine immutable old-package resources without selecting upgrade paths or supplying GUID mappings'}
(out / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
hashes = {p.relative_to(out).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
          for p in out.rglob('*') if p.is_file() and p != out / 'sha256.json'}
(out / 'sha256.json').write_text(json.dumps(hashes, sort_keys=True, indent=2) + '\n')
print(json.dumps({'completed_scopes': [r['scope'] for r in records], 'files': len(hashes) + 1}, indent=2))

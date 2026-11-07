from pathlib import Path
from datetime import datetime, timezone
import gzip
import hashlib
import json
import os
import shutil
import subprocess
import sys
from zipfile import ZipFile

scope = sys.argv[1]
owners = {'acc': ['AdvancedCostumeController'], 'ao': ['AnimatorOptimizer'],
          'ass': ['AvatarSecuritySystem'], 'viseme': ['VisemeBlendshapeOverride'],
          'all': ['AdvancedCostumeController', 'AvatarSecuritySystem', 'VisemeBlendshapeOverride', 'AnimatorOptimizer']}[scope]
repo = Path('/workspace/UnityBox')
base = Path('/workspace/unitybox-setup/unity')
here = Path(__file__).resolve().parent
producer = repo / 'Docs/Verification/ComponentReferences/LegacyUpgrade/Scopes' / scope
project = base / 'projects' / ('whole-assets-replay-' + scope + '-20261108')
prepare = [sys.executable, 'scripts/unity/test_project.py', 'prepare', '--repo', str(repo),
           '--project', str(project), '--profile', 'vrc', '--offline',
           '--dependency-cache', str(base / 'dependencies'),
           '--dependency', 'nadena.dev.ndmf', '--dependency', 'nadena.dev.modular-avatar']
for owner in owners:
    prepare += ['--package', owner]
output = here / scope
output.mkdir(exist_ok=True)
with (output / 'prepare.json').open('w') as stream:
    subprocess.run(prepare, cwd=repo, stdout=stream, check=True)
marker = json.loads((project / '.unitybox-test-project.json').read_text())
assert sorted(Path(info['source']).name for info in marker['packages'].values() if info['origin'] == 'local') == sorted(owners)
assert 'extra_sources' not in marker
manifest_path = project / 'Packages/manifest.json'
manifest = json.loads(manifest_path.read_text())
manifest.pop('testables', None)
manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
site = Path(json.loads((base / 'whole-assets-vpm-local.json').read_text())['output']) / 'site'
zip_inputs = []
for package, info in marker['packages'].items():
    if info['origin'] != 'local':
        continue
    archive_path = site / (package + '-' + info['version'] + '.zip')
    target = project / 'Packages' / package
    shutil.rmtree(target)
    target.mkdir()
    with ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        archive.extractall(target)
    zip_inputs.append({'package': package, 'version': info['version'], 'sha256': hashlib.sha256(archive_path.read_bytes()).hexdigest()})
evidence = project / '.unitybox-component-upgrade'
evidence.mkdir()
relocations = {'Assets/ComponentUpgradeFixtures/Base.prefab': 'Assets/OldCostumes/Base.prefab',
               'Assets/ComponentUpgradeFixtures/Variant.prefab': 'Assets/OtherCollection/Variant.prefab',
               'Assets/ComponentUpgradeFixtures/Nested.prefab': 'Assets/Containers/Nested.prefab',
               'Assets/ComponentUpgradeFixtures/Avatar.unity': 'Assets/Scenes/Avatar.unity'}
def relocated(path):
    for old, new in relocations.items():
        if path == old or path == old + '.meta':
            return new + path[len(old):]
    return path
state = json.loads((producer / 'legacy-state.json').read_text())
records = []
for record in state['files']:
    original_path = record['path']
    data = gzip.decompress((producer / 'original' / (original_path + '.gz')).read_bytes())
    assert hashlib.sha256(data).hexdigest() == record['sha256']
    original = project / record['original']
    original.parent.mkdir(parents=True, exist_ok=True)
    original.write_bytes(data)
    duplicate = next((row for row in state['duplicates'] if row['asset'] == original_path), None)
    if duplicate:
        data = gzip.decompress((producer / ('duplicate-before-apply-' + duplicate['owner'] + '.unity.gz')).read_bytes())
        baseline = project / duplicate['currentBytes']
        baseline.parent.mkdir(parents=True, exist_ok=True)
        baseline.write_bytes(data)
    record['path'] = relocated(original_path)
    destination = project / record['path']
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(data)
    records.append({'original_path': original_path, 'path': record['path'], 'sha256': hashlib.sha256(data).hexdigest()})
for record in state['components']:
    record['asset'] = relocated(record['asset'])
(evidence / 'legacy-state.json').write_text(json.dumps(state, indent=2) + '\n')
(evidence / 'config.json').write_text(json.dumps({'scope': scope, 'owners': owners}, indent=2) + '\n')
editor = project / 'Assets/Editor'
editor.mkdir(exist_ok=True)
shutil.copy2(here / 'ComponentUpgradeFixture.cs', editor / 'ComponentUpgradeFixture.cs')
shutil.copy2(here / 'ComponentUpgradeReferenceHolder.cs', project / 'Assets/ComponentUpgradeReferenceHolder.cs')
shutil.copy2(producer / 'generated-script-metadata/ComponentUpgradeReferenceHolder.cs.meta', project / 'Assets/ComponentUpgradeReferenceHolder.cs.meta')
(project / 'Assets/SelectionSentinel.txt').write_text('Unrelated current selection; no migration path selection was supplied.\n')
provenance = {'scope': scope, 'producer_revision': 'e62c741f7921071834924f75cbfd80f1a50a5304',
              'producer_summary_sha256': hashlib.sha256((producer / 'summary.json').read_bytes()).hexdigest() if (producer / 'summary.json').is_file() else None,
              'original_producer_state_sha256': hashlib.sha256((producer / 'legacy-state.json').read_bytes()).hexdigest(),
              'relocated_without_changing_asset_bytes_or_guids': relocations, 'restored_files': records, 'current_zip_inputs': zip_inputs}
(output / 'input-provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
env = os.environ.copy()
for key in ['CONFIG', 'DATA', 'CACHE']:
    env['XDG_' + key + '_HOME'] = str(base / 'interactive' / key.lower())
command = [str(base / 'editor/opt/unity/Editor/Unity'), '-batchmode', '-nographics',
           '-projectPath', str(project), '-executeMethod', 'ComponentUpgradeFixture.ReplayWholeAssets',
           '-logFile', str(output / 'editor.log')]
record = {'command': command, 'started_utc': datetime.now(timezone.utc).isoformat()}
with (output / 'launcher.log').open('w') as stream:
    process = subprocess.run(command, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=1200)
result_path = evidence / 'migrated.json'
record.update(exit_code=process.returncode, finished_utc=datetime.now(timezone.utc).isoformat(),
              result=json.loads(result_path.read_text()) if result_path.is_file() else None)
record['passed'] = process.returncode == 0 and record['result'] and record['result']['passed']
(output / 'run.json').write_text(json.dumps(record, indent=2) + '\n')
print(json.dumps({'scope': scope, 'passed': bool(record['passed']), 'exit_code': process.returncode, 'result': str(result_path)}, indent=2))
raise SystemExit(0 if record['passed'] else 1)

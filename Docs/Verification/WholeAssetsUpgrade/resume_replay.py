from pathlib import Path
from datetime import datetime, timezone
import json
import os
import shutil
import subprocess
import sys

scope = sys.argv[1]
base = Path('/workspace/unitybox-setup/unity')
here = Path(__file__).resolve().parent
project = base / 'projects' / ('whole-assets-replay-' + scope + '-20261108')
output = here / scope
result_file = project / '.unitybox-component-upgrade/migrated.json'
if result_file.exists():
    previous = json.loads(result_file.read_text())
    assert not previous['passed'] or '--refresh' in sys.argv
    attempt = output / ('preapply-fixture-attempt-' + datetime.now(timezone.utc).strftime('%H%M%S'))
    attempt.mkdir()
    shutil.copy2(result_file, attempt / 'native.json')
    for name in ['editor.log', 'launcher.log', 'run.json']:
        if (output / name).exists(): shutil.copy2(output / name, attempt / name)
    shutil.copy2(project / 'Assets/Editor/ComponentUpgradeFixture.cs', attempt / 'ComponentUpgradeFixture.cs')
# Import the final current ZIP before repeating the identical genuine old-resource batch.
if '--refresh' in sys.argv:
    from zipfile import ZipFile
    import hashlib
    provenance_path = output / 'input-provenance.json'
    provenance = json.loads(provenance_path.read_text())
    site = Path(json.loads((base / 'whole-assets-vpm-local.json').read_text())['output']) / 'site'
    for item in provenance['current_zip_inputs']:
        archive = site / (item['package'] + '-' + item['version'] + '.zip')
        target = project / 'Packages' / item['package']
        shutil.rmtree(target)
        target.mkdir()
        with ZipFile(archive) as zipped: zipped.extractall(target)
        item['sha256'] = hashlib.sha256(archive.read_bytes()).hexdigest()
    provenance_path.write_text(json.dumps(provenance, indent=2) + '\n')
result_file.unlink(missing_ok=True)
# Restore the exact producer-derived input before repeating the batch.
import gzip
provenance = json.loads((output / 'input-provenance.json').read_text())
producer = Path('/workspace/UnityBox/Docs/Verification/ComponentReferences/LegacyUpgrade/Scopes') / scope
for item in provenance['restored_files']:
    original = item['original_path']
    source = producer / 'original' / (original + '.gz')
    data = gzip.decompress(source.read_bytes())
    if original.endswith('.unity') and '/Duplicate-' in original:
        owner = Path(original).stem.removeprefix('Duplicate-')
        data = gzip.decompress((producer / ('duplicate-before-apply-' + owner + '.unity.gz')).read_bytes())
    (project / item['path']).write_bytes(data)
shutil.copy2(here / 'ComponentUpgradeFixture.cs', project / 'Assets/Editor/ComponentUpgradeFixture.cs')
env = os.environ.copy()
for key in ['CONFIG', 'DATA', 'CACHE']:
    env['XDG_' + key + '_HOME'] = str(base / 'interactive' / key.lower())
command = [str(base / 'editor/opt/unity/Editor/Unity'), '-batchmode', '-nographics',
           '-projectPath', str(project), '-executeMethod', 'ComponentUpgradeFixture.ReplayWholeAssets',
           '-logFile', str(output / 'editor.log')]
record = {'command': command, 'started_utc': datetime.now(timezone.utc).isoformat()}
with (output / 'launcher.log').open('w') as stream:
    process = subprocess.run(command, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=1200)
record.update(exit_code=process.returncode, finished_utc=datetime.now(timezone.utc).isoformat(),
              result=json.loads(result_file.read_text()))
record['passed'] = process.returncode == 0 and record['result']['passed']
(output / 'run.json').write_text(json.dumps(record, indent=2) + '\n')
print(json.dumps({'scope': scope, 'passed': record['passed'], 'exit_code': process.returncode}, indent=2))
raise SystemExit(0 if record['passed'] else 1)

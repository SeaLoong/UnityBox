#!/usr/bin/env python3
"""Run an owned project's real multi-plugin old-package and explicit migration regression."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import wave
from zipfile import ZipFile


PACKAGES = {
    'top.sealoong.unitybox.advanced-costume-controller',
    'top.sealoong.unitybox.avatar-security-system',
    'top.sealoong.unitybox.viseme-blendshape-override',
    'top.sealoong.unitybox.animator-optimizer',
}


def named(values):
    result = {}
    for value in values:
        key, separator, path = value.partition('=')
        if not separator or key in result:
            raise ValueError('Use unique PACKAGE=PATH arguments: ' + value)
        result[key] = Path(path).resolve()
    if set(result) != PACKAGES:
        raise ValueError('This fixture requires the four ACC, ASS, Viseme and AnimatorOptimizer packages')
    return result


def sha(data):
    return hashlib.sha256(data).hexdigest()


def inspect_input(name, path, development_source=None, omit_development_tests=False):
    if path.is_dir():
        files = {}
        for item in sorted(path.rglob('*')):
            if item.is_symlink():
                raise ValueError('Directory inputs must contain original files, not symlinks: ' + str(item))
            if item.is_file():
                files[item.relative_to(path).as_posix()] = item.read_bytes()
        archive_sha = None
    else:
        with ZipFile(path) as archive:
            if archive.testzip() is not None:
                raise ValueError('ZIP CRC failed: ' + str(path))
            files = {}
            for info in archive.infolist():
                relative = Path(info.filename)
                if relative.is_absolute() or '..' in relative.parts or info.is_dir():
                    if info.is_dir() and not relative.is_absolute() and '..' not in relative.parts:
                        continue
                    raise ValueError('Invalid package path: ' + info.filename)
                if info.filename in files:
                    raise ValueError('Duplicate ZIP entry: ' + info.filename)
                files[info.filename] = archive.read(info)
        archive_sha = sha(path.read_bytes())
    manifest = json.loads(files['package.json'].decode('utf-8-sig'))
    if manifest['name'] != name:
        raise ValueError('Package identity differs: ' + str(path))
    original_identities = [(name, sha(data)) for name, data in sorted(files.items())]
    excluded = []
    if omit_development_tests:
        if not name.endswith('.animator-optimizer') or development_source is None:
            raise ValueError('Only the explicitly identified unpublished AO development tests may be omitted')
        excluded = [relative for relative in files if relative == 'Tests.meta' or relative.startswith('Tests/')]
        files = {relative: data for relative, data in files.items() if relative not in excluded}
    identities = [(name, sha(data)) for name, data in sorted(files.items())]
    record = {'source': str(path), 'package': name, 'version': manifest['version'],
              'input_kind': 'directory snapshot' if path.is_dir() else 'ZIP',
              'archive_sha256': archive_sha,
              'tree_sha256': sha(json.dumps(identities, separators=(',', ':')).encode()),
              'original_tree_sha256': sha(json.dumps(original_identities, separators=(',', ':')).encode()),
              'excluded_development_test_files': excluded,
              'files': len(files), 'metadata_files': [name for name in files if name.endswith('.meta')]}
    if development_source is not None and name.endswith('.animator-optimizer'):
        record.update(published=False, development_source=development_source)
    return files, record


def replace(project, packages, marker, ao_source=None, expected=None, omit_ao_tests=False):
    records = []
    for name, source in packages.items():
        if marker['packages'].get(name, {}).get('origin') != 'local':
            raise ValueError('Package is not local in this owned test project: ' + name)
        files, record = inspect_input(name, source, ao_source, omit_ao_tests and name.endswith('.animator-optimizer'))
        if expected and name in expected and record['archive_sha256'] != expected[name]:
            raise ValueError('Published input SHA256 differs: ' + name)
        target = project / 'Packages' / name
        if target.is_symlink():
            raise ValueError('Refusing to replace a symlinked package: ' + str(target))
        if target.exists():
            shutil.rmtree(target)
        target.mkdir()
        for relative, data in files.items():
            destination = target / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
        records.append(record)
    return records


def phase(args, name, method, inputs):
    output = args.project / '.unitybox-component-upgrade'
    env = os.environ.copy()
    for kind in ('CONFIG', 'DATA', 'CACHE'):
        folder = args.xdg_root / kind.lower()
        folder.mkdir(parents=True, exist_ok=True)
        env['XDG_' + kind + '_HOME'] = str(folder)
    command = [str(args.unity), '-batchmode', '-nographics', '-projectPath', str(args.project),
               '-executeMethod', 'ComponentUpgradeFixture.' + method,
               '-logFile', str(output / (name + '-editor.log'))]
    record = {'phase': name, 'command': command, 'inputs': inputs,
              'started_at_utc': datetime.now(timezone.utc).isoformat()}
    with (output / (name + '-launcher.log')).open('w') as log:
        process = subprocess.Popen(command, cwd=args.project, env=env, stdout=log,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        try:
            record['exit_code'] = process.wait(timeout=args.timeout)
        except (subprocess.TimeoutExpired, KeyboardInterrupt):
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
            raise
    result = output / (name + '.json')
    if result.is_file():
        record['result'] = json.loads(result.read_text())
    record['passed'] = record['exit_code'] == 0 and record.get('result', {}).get('passed') is True
    for source_kind, source in [('fixture', args.project / 'Assets/Editor/ComponentUpgradeFixture.cs'),
                                ('holder', args.project / 'Assets/ComponentUpgradeReferenceHolder.cs')]:
        destination = output / 'source-snapshots' / name / source.name
        destination.parent.mkdir(parents=True, exist_ok=True)
        data = source.read_bytes()
        destination.write_bytes(data)
        record[source_kind + '_source'] = {'sha256': sha(data), 'file': str(destination)}
    if record['passed'] and name == 'legacy':
        state = json.loads((output / 'legacy-state.json').read_text())
        record['generated_script_metadata'] = []
        for item in state['types']:
            source = (args.project / (item['scriptPath'] + '.meta')).resolve()
            if not source.is_relative_to(args.project / 'Packages'):
                raise ValueError('Fixture reported a script outside its owned packages')
            data = source.read_bytes()
            if sha(data) != item['generatedMetaSha256']:
                raise ValueError('Generated script metadata differs from the native legacy snapshot')
            destination = output / 'generated-script-metadata' / (item['type'] + '.cs.meta')
            destination.parent.mkdir(exist_ok=True)
            destination.write_bytes(data)
            record['generated_script_metadata'].append({'type': item['type'], 'guid': item['guid'],
                                                       'sha256': sha(data), 'file': str(destination)})
    record['finished_at_utc'] = datetime.now(timezone.utc).isoformat()
    (output / (name + '-run.json')).write_text(json.dumps(record, indent=2) + '\n')
    if not record['passed']:
        raise RuntimeError('Phase failed; inspect ' + str(output / (name + '-run.json')))
    return record


def cold_upgrade_import(project, output):
    """Discard only this owned fixture's rebuildable import cache, retaining warm DLL proof."""
    state = json.loads((output / 'legacy-state.json').read_text())
    before = []
    for item in state['files']:
        digest = sha((project / item['path']).read_bytes())
        if digest != item['sha256']:
            raise ValueError('Legacy asset changed before cold import: ' + item['path'])
        before.append({'path': item['path'], 'sha256': digest})
    library = project / 'Library'
    if library.is_symlink() or library.resolve().parent != project.resolve():
        raise ValueError('Unexpected fixture Library path')
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    preserved = project / '.unitybox-native-assemblies' / ('warm-before-cold-upgrade-' + stamp)
    assemblies = library / 'ScriptAssemblies'
    dlls = []
    if assemblies.is_dir():
        shutil.copytree(assemblies, preserved)
        dlls = [{'path': str(path.relative_to(preserved)), 'sha256': sha(path.read_bytes())}
                for path in sorted(preserved.rglob('*')) if path.is_file()]
    existed = library.exists()
    if existed:
        shutil.rmtree(library)
    for item in before:
        if sha((project / item['path']).read_bytes()) != item['sha256']:
            raise ValueError('Legacy asset changed while removing owned cache: ' + item['path'])
    proof = {
        'recorded_at_utc': datetime.now(timezone.utc).isoformat(),
        'reason': 'Cold upgrade import checks persisted YAML GUIDs without stale warm import artifacts.',
        'removed_only': str(library), 'library_existed': existed,
        'warm_assemblies_preserved_in': str(preserved),
        'warm_assembly_files': dlls,
        'warm_assembly_scope': 'Immediately before cold import; may include current-package compilation from retained diagnostics, not claimed to be legacy phase binaries.',
        'all_original_files_unchanged_before_and_after_cache_removal': before,
    }
    (output / 'cold-upgrade-import.json').write_text(json.dumps(proof, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--unity', type=Path, required=True)
    parser.add_argument('--xdg-root', type=Path, required=True)
    parser.add_argument('--legacy-package', action='append', default=[], metavar='NAME=ZIP_OR_DIR')
    parser.add_argument('--current-package', action='append', default=[], metavar='NAME=ZIP_OR_DIR')
    parser.add_argument('--expected-legacy-sha', action='append', default=[], metavar='NAME=SHA256')
    parser.add_argument('--legacy-ao-source', required=True, help='Exact development commit/source; AO 0.2 was not publicly released')
    parser.add_argument('--legacy-ao-production-only', action='store_true', help='Exclude only unpublished AO development Tests; retain all original Runtime/Editor source and metadata')
    parser.add_argument('--migration-tool', type=Path, help='Standalone tool root containing Editor, or its Editor directory')
    parser.add_argument('--stop-after', choices=('legacy', 'broken', 'migrated'), default='migrated')
    parser.add_argument('--resume-from', choices=('broken', 'migrated'), help='Resume a retained successful phase without recreating its assets')
    parser.add_argument('--cold-upgrade-import', action='store_true', help='Preserve warm ScriptAssemblies then rebuild only this owned fixture Library before current-package import')
    parser.add_argument('--timeout', type=float, default=1200)
    args = parser.parse_args()
    for name in ('project', 'unity', 'xdg_root'):
        setattr(args, name, getattr(args, name).resolve())
    if args.migration_tool is not None:
        args.migration_tool = args.migration_tool.resolve()
    marker = json.loads((args.project / '.unitybox-test-project.json').read_text())
    repo = Path(marker['repo']).resolve()
    if args.project == repo or args.project.is_relative_to(repo):
        raise ValueError('Use a disposable owned project outside the source checkout')
    output = args.project / '.unitybox-component-upgrade'
    if output.exists() and args.resume_from is None:
        raise ValueError('Use a fresh project; previous evidence must be retained: ' + str(output))
    if args.resume_from is not None and not (output / 'legacy-state.json').is_file():
        raise ValueError('A successful retained legacy phase is required for resuming')
    output.mkdir(exist_ok=True)
    # This integration uses executeMethod, not the package Test Runner. Consumers
    # do not opt into package tests merely by importing an authoring package.
    manifest_path = args.project / 'Packages/manifest.json'
    project_manifest = json.loads(manifest_path.read_text())
    testables = project_manifest.pop('testables', [])
    if testables:
        manifest_path.write_text(json.dumps(project_manifest, indent=2) + '\n')
        (output / 'project-manifest-adjustment.json').write_text(json.dumps({
            'package_testables_removed_for_execute_method_fixture': testables,
            'legacy_package_archives_and_sources_changed': False
        }, indent=2) + '\n')
    here = Path(__file__).resolve().parent
    editor = args.project / 'Assets/Editor/ComponentUpgradeFixture.cs'
    editor.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(here / 'ComponentUpgradeFixture.cs', editor)
    shutil.copy2(here / 'ComponentUpgradeReferenceHolder.cs', args.project / 'Assets/ComponentUpgradeReferenceHolder.cs')
    if args.resume_from is None:
        assets = args.project / 'Assets/ComponentUpgradeFixtures'
        assets.mkdir(parents=True, exist_ok=True)
        for name in ('Warning', 'Success'):
            with wave.open(str(assets / (name + '.wav')), 'wb') as audio:
                audio.setnchannels(1); audio.setsampwidth(2); audio.setframerate(8000)
                audio.writeframes(b'\0\0' * 800)
    expected = {}
    for value in args.expected_legacy_sha:
        name, separator, digest = value.partition('=')
        if not separator or len(digest) != 64:
            raise ValueError('Use PACKAGE=SHA256 for expected input hashes')
        expected[name] = digest.lower()
    if args.resume_from is None:
        legacy_inputs = replace(args.project, named(args.legacy_package), marker, args.legacy_ao_source, expected, args.legacy_ao_production_only)
        phases = [phase(args, 'legacy', 'CreateLegacy', legacy_inputs)]
    else:
        previous = json.loads((output / 'legacy-run.json').read_text())
        if not previous['passed']:
            raise ValueError('The previous legacy phase did not pass')
        phases = [previous]
    if args.stop_after != 'legacy':
        if args.migration_tool is None:
            raise ValueError('Supply --migration-tool before running the current-package phases')
        if args.resume_from != 'migrated':
            current_inputs = replace(args.project, named(args.current_package), marker)
            if args.cold_upgrade_import:
                cold_upgrade_import(args.project, output)
        else:
            previous = json.loads((output / 'broken-run.json').read_text())
            if not previous['passed']:
                raise ValueError('The previous broken phase did not pass')
            current_inputs = previous['inputs']
        target = args.project / 'Assets/UnityBox/ComponentReferenceMigration'
        if target.exists():
            # test_project.prepare installs this tool at the canonical Assets path.
            # Replace its owned Editor/test copy instead of introducing a duplicate asmdef.
            existing_assembly = target / 'Editor/UnityBox.ComponentReferenceMigration.Editor.asmdef'
            if not existing_assembly.is_file() or json.loads(existing_assembly.read_text()).get('name') != 'UnityBox.ComponentReferenceMigration.Editor':
                raise ValueError('Unexpected content at the owned migration tool target: ' + str(target))
            shutil.rmtree(target)
        if (args.migration_tool / 'Editor').is_dir():
            shutil.copytree(args.migration_tool / 'Editor', target / 'Editor')
        else:
            shutil.copytree(args.migration_tool, target / 'Editor')
        if args.resume_from != 'migrated':
            phases.append(phase(args, 'broken', 'VerifyBroken', current_inputs))
        else:
            phases.append(previous)
        if args.stop_after == 'migrated':
            phases.append(phase(args, 'migrated', 'ApplyAndVerify', current_inputs))
    summary = {'passed': True, 'scope': 'Synthetic owned test project; explicit single batch Apply only',
               'fixture_sha256': sha((here / 'ComponentUpgradeFixture.cs').read_bytes()),
               'holder_sha256': sha((here / 'ComponentUpgradeReferenceHolder.cs').read_bytes()),
               'phases': phases}
    filename = 'three-stage-summary.json' if len(phases) == 3 else 'suite-state.json'
    (output / filename).write_text(json.dumps(summary, indent=2) + '\n')
    print(json.dumps({'passed': True, 'output': str(output), 'phases': len(phases),
                      'paused': len(phases) < 3}, indent=2))


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Run a ZIP-only, plugin-owned component-reference upgrade in an owned test project."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import wave
from zipfile import ZipFile


PLUGINS = {
    'acc': ('top.sealoong.unitybox.advanced-costume-controller', 'AdvancedCostumeController'),
    'ass': ('top.sealoong.unitybox.avatar-security-system', 'AvatarSecuritySystem'),
    'viseme': ('top.sealoong.unitybox.viseme-blendshape-override', 'VisemeBlendshapeOverride'),
    'ao': ('top.sealoong.unitybox.animator-optimizer', 'AnimatorOptimizer'),
}
AO_PACKAGE = PLUGINS['ao'][0]
UNITYBOX_PREFIX = 'top.sealoong.unitybox.'
SHARED_ASSEMBLY_PREFIX = 'UnityBox.ComponentReferenceMigration'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def named(values, selected):
    result = {}
    for value in values:
        key, separator, path = value.partition('=')
        if not separator or not path or key in result:
            raise ValueError('Use unique PACKAGE=PATH arguments: ' + value)
        result[key] = Path(path).resolve()
    if set(result) != set(selected):
        raise ValueError('Inputs must exactly match the selected scope: ' + ', '.join(selected))
    return result


def assert_project_isolation(project, selected):
    """Check that the consumer imports only selected UnityBox packages and their own code."""
    selected = set(selected)
    for relative in ('Assets/UnityBox/ComponentReferenceMigration', 'Assets/UnityBoxComponentMigration'):
        if (project / relative).exists():
            raise ValueError('Shared migration source is installed in this consumer: ' + relative)
    installed = {}
    for path in sorted((project / 'Packages').glob('*/package.json')):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        name = data['name']
        if name.startswith(UNITYBOX_PREFIX):
            installed[name] = {'version': data['version'], 'path': str(path.parent)}
    if set(installed) != selected:
        raise ValueError('Consumer UnityBox package set differs from its scope: ' + ', '.join(sorted(installed)))
    manifest = json.loads((project / 'Packages/manifest.json').read_text())
    declared = {name for name in manifest.get('dependencies', {}) if name.startswith(UNITYBOX_PREFIX)}
    if declared != selected:
        raise ValueError('Consumer manifest declares unexpected UnityBox packages: ' + ', '.join(sorted(declared)))
    for root in (project / 'Assets', project / 'Packages'):
        for definition in sorted(root.rglob('*.asmdef')):
            data = json.loads(definition.read_text(encoding='utf-8-sig'))
            if data.get('name', '').startswith(SHARED_ASSEMBLY_PREFIX):
                raise ValueError('Shared migration assembly is installed in this consumer: ' + str(definition))
    return {
        'passed': True, 'selected_unitybox_packages': sorted(selected),
        'installed_unitybox_packages': installed,
        'shared_migration_source_present': False, 'shared_migration_assembly_present': False,
    }


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
    for kind in ('dependencies', 'vpmDependencies'):
        peers = [dependency for dependency in manifest.get(kind, {})
                 if dependency.startswith(UNITYBOX_PREFIX) and dependency != name]
        if peers:
            raise ValueError('Plugin declares a peer UnityBox dependency: ' + name + ': ' + ', '.join(peers))
    original_identities = [(relative, sha(data)) for relative, data in sorted(files.items())]
    excluded = []
    if omit_development_tests:
        if name != AO_PACKAGE or development_source is None or not path.is_dir():
            raise ValueError('Only the explicitly identified unpublished AO development tests may be omitted')
        excluded = [relative for relative in files if relative == 'Tests.meta' or relative.startswith('Tests/')]
        files = {relative: data for relative, data in files.items() if relative not in excluded}
    identities = [(relative, sha(data)) for relative, data in sorted(files.items())]
    owner = next(owner for package, owner in PLUGINS.values() if package == name)
    namespace = ('namespace UnityBox.' + owner + '.ComponentMigration').encode()
    migration_sources = [
        {'path': relative, 'sha256': sha(data), 'declares_engine': re.search(rb'\bclass\s+MigrationEngine\b', data) is not None}
        for relative, data in sorted(files.items())
        if relative.endswith('.cs') and namespace in data
    ]
    record = {
        'source': str(path), 'package': name, 'owner': owner, 'version': manifest['version'],
        'input_kind': 'directory snapshot' if path.is_dir() else 'ZIP',
        'archive_sha256': archive_sha, 'zip_crc_checked': not path.is_dir(),
        'tree_sha256': sha(json.dumps(identities, separators=(',', ':')).encode()),
        'original_tree_sha256': sha(json.dumps(original_identities, separators=(',', ':')).encode()),
        'excluded_development_test_files': excluded,
        'files': len(files), 'metadata_files': [relative for relative in files if relative.endswith('.meta')],
        'dependencies': manifest.get('dependencies', {}),
        'vpmDependencies': manifest.get('vpmDependencies', {}),
        'embedded_migration_namespace': 'UnityBox.' + owner + '.ComponentMigration',
        'migration_source_files': migration_sources,
    }
    if development_source is not None and name == AO_PACKAGE:
        record.update(published=False, development_source=development_source)
    return files, record


def replace(project, packages, marker, ao_source=None, expected=None, omit_ao_tests=False, current=False):
    prepared = []
    # Validate all input archives before replacing any selected package copy.
    for name, source in packages.items():
        if marker['packages'].get(name, {}).get('origin') != 'local':
            raise ValueError('Package is not local in this owned test project: ' + name)
        if current and not source.is_file():
            raise ValueError('Current plugin updates must be complete ZIP inputs: ' + str(source))
        if not current and name != AO_PACKAGE and not source.is_file():
            raise ValueError('Published legacy inputs must be complete ZIP archives: ' + str(source))
        if not current and name == AO_PACKAGE and not source.is_dir():
            raise ValueError('The unpublished AO legacy input must be its exact development source directory')
        files, record = inspect_input(name, source, ao_source, omit_ao_tests and name == AO_PACKAGE)
        if expected and name in expected and record['archive_sha256'] != expected[name]:
            raise ValueError('Published input SHA256 differs: ' + name)
        if current and not any(source['declares_engine'] for source in record['migration_source_files']):
            raise ValueError('Plugin ZIP does not contain its own migration engine: ' + name)
        target = project / 'Packages' / name
        if target.is_symlink():
            raise ValueError('Refusing to replace a symlinked package: ' + str(target))
        prepared.append((target, files, record))
    for target, files, _ in prepared:
        if target.exists():
            shutil.rmtree(target)
        target.mkdir()
        for relative, data in files.items():
            destination = target / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
    return [record for _, _, record in prepared]


def snapshot_legacy_assemblies(project):
    source = project / 'Library/ScriptAssemblies'
    destination = project / '.unitybox-native-assemblies/legacy'
    if not source.is_dir() or source.is_symlink():
        raise ValueError('Successful legacy phase did not leave owned native ScriptAssemblies')
    if destination.exists():
        raise ValueError('Legacy assembly evidence already exists: ' + str(destination))
    shutil.copytree(source, destination)
    files = [{'path': str(path.relative_to(destination)), 'sha256': sha(path.read_bytes())}
             for path in sorted(destination.rglob('*')) if path.is_file()]
    return {'directory': str(destination), 'files': files,
            'captured_at_utc': datetime.now(timezone.utc).isoformat(),
            'scope': 'Successful native legacy phase, before any current-package replacement/import'}


def phase(args, name, method, inputs, selected):
    output = args.project / '.unitybox-component-upgrade'
    isolation = assert_project_isolation(args.project, selected)
    config = output / 'config.json'
    config_sha = sha(config.read_bytes())
    env = os.environ.copy()
    for kind in ('CONFIG', 'DATA', 'CACHE'):
        folder = args.xdg_root / kind.lower()
        folder.mkdir(parents=True, exist_ok=True)
        env['XDG_' + kind + '_HOME'] = str(folder)
    command = [str(args.unity), '-batchmode', '-nographics', '-projectPath', str(args.project),
               '-executeMethod', 'ComponentUpgradeFixture.' + method,
               '-logFile', str(output / (name + '-editor.log'))]
    record = {'phase': name, 'scope': args.scope, 'command': command, 'inputs': inputs,
              'project_package_isolation': isolation, 'config_sha256': config_sha,
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
    if sha(config.read_bytes()) != config_sha:
        raise ValueError('Native fixture modified its selected-owner configuration')
    for source_kind, source in [('fixture', args.project / 'Assets/Editor/ComponentUpgradeFixture.cs'),
                                ('holder', args.project / 'Assets/ComponentUpgradeReferenceHolder.cs')]:
        destination = output / 'source-snapshots' / name / source.name
        destination.parent.mkdir(parents=True, exist_ok=True)
        data = source.read_bytes()
        destination.write_bytes(data)
        record[source_kind + '_source'] = {'sha256': sha(data), 'file': str(destination)}
    if record['passed'] and name == 'legacy':
        state_path = output / 'legacy-state.json'
        state = json.loads(state_path.read_text())
        created_state = output / 'legacy-state-created.json'
        if created_state.exists():
            raise ValueError('Original legacy state evidence already exists: ' + str(created_state))
        shutil.copy2(state_path, created_state)
        record['legacy_state_created'] = {'file': str(created_state), 'sha256': sha(created_state.read_bytes())}
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
        source = args.project / 'Assets/ComponentUpgradeReferenceHolder.cs.meta'
        data = source.read_bytes()
        destination = output / 'generated-script-metadata/ComponentUpgradeReferenceHolder.cs.meta'
        destination.write_bytes(data)
        record['fixture_helper_metadata'] = {'source': str(source), 'file': str(destination),
                                            'sha256': sha(data)}
        record['legacy_assemblies'] = snapshot_legacy_assemblies(args.project)
    record['finished_at_utc'] = datetime.now(timezone.utc).isoformat()
    write_json(output / (name + '-run.json'), record)
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
        'warm_assemblies_preserved_in': str(preserved), 'warm_assembly_files': dlls,
        'warm_assembly_scope': 'Immediately before cold import; original phase-one binaries are separately preserved in legacy-run.json.legacy_assemblies.',
        'all_original_files_unchanged_before_and_after_cache_removal': before,
    }
    write_json(output / 'cold-upgrade-import.json', proof)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--unity', type=Path, required=True)
    parser.add_argument('--xdg-root', type=Path, required=True)
    parser.add_argument('--scope', choices=(*PLUGINS, 'all'), default='all',
                        help='Install only this UnityBox plugin, or all four together')
    parser.add_argument('--legacy-package', action='append', default=[], metavar='NAME=ZIP_OR_DIR')
    parser.add_argument('--current-package', action='append', default=[], metavar='NAME=ZIP')
    parser.add_argument('--expected-legacy-sha', action='append', default=[], metavar='NAME=SHA256')
    parser.add_argument('--legacy-ao-source', help='Required when AO is selected: exact unpublished development commit/source')
    parser.add_argument('--legacy-ao-production-only', action='store_true',
                        help='Exclude only unpublished AO development Tests; retain original Runtime/Editor source and metadata')
    parser.add_argument('--stop-after', choices=('legacy', 'broken', 'migrated'), default='migrated')
    parser.add_argument('--resume-from', choices=('broken', 'migrated'), help='Resume retained successful legacy/current phases')
    parser.add_argument('--cold-upgrade-import', action='store_true',
                        help='Retain warm ScriptAssemblies and rebuild only this owned fixture Library before current ZIP import')
    parser.add_argument('--timeout', type=float, default=1200)
    args = parser.parse_args()
    for name in ('project', 'unity', 'xdg_root'):
        setattr(args, name, getattr(args, name).resolve())
    selections = list(PLUGINS.values()) if args.scope == 'all' else [PLUGINS[args.scope]]
    selected = [package for package, _ in selections]
    owners = [owner for _, owner in selections]
    if AO_PACKAGE in selected and not args.legacy_ao_source:
        raise ValueError('Supply --legacy-ao-source for the selected unpublished AO legacy input')
    if args.resume_from is None and AO_PACKAGE in selected and not args.legacy_ao_production_only:
        raise ValueError('Supply --legacy-ao-production-only for the unpublished AO production-source fixture')
    if args.resume_from == 'migrated' and args.stop_after != 'migrated':
        raise ValueError('--resume-from migrated requires --stop-after migrated')
    marker = json.loads((args.project / '.unitybox-test-project.json').read_text())
    if marker.get('tool') != 'UnityBox.test_project':
        raise ValueError('Use a project prepared by UnityBox.test_project')
    repo = Path(marker['repo']).resolve()
    if args.project == repo or args.project.is_relative_to(repo):
        raise ValueError('Use a disposable owned project outside the source checkout')
    assert_project_isolation(args.project, selected)
    output = args.project / '.unitybox-component-upgrade'
    if output.exists() and args.resume_from is None:
        raise ValueError('Use a fresh project; previous evidence must be retained: ' + str(output))
    if args.resume_from is not None and not (output / 'legacy-state.json').is_file():
        raise ValueError('A successful retained legacy phase is required for resuming')
    expected = {}
    for value in args.expected_legacy_sha:
        name, separator, digest = value.partition('=')
        if not separator or name in expected or len(digest) != 64 or any(ch not in '0123456789abcdefABCDEF' for ch in digest):
            raise ValueError('Use unique PACKAGE=SHA256 for expected input hashes')
        expected[name] = digest.lower()
    legacy_packages = None
    if args.resume_from is None:
        legacy_packages = named(args.legacy_package, selected)
        published = set(selected) - {AO_PACKAGE}
        if set(expected) != published:
            raise ValueError('Supply exactly one expected SHA256 for every selected published legacy ZIP')
    current_packages = None
    if args.stop_after != 'legacy' and args.resume_from != 'migrated':
        current_packages = named(args.current_package, selected)
    output.mkdir(exist_ok=True)
    config = {'scope': args.scope, 'owners': owners}
    config_path = output / 'config.json'
    if args.resume_from is not None:
        if json.loads(config_path.read_text()) != config:
            raise ValueError('Resume scope differs from the original native fixture configuration')
    else:
        write_json(config_path, config)
    # This integration uses executeMethod, independently of package Test Runner opt-in.
    manifest_path = args.project / 'Packages/manifest.json'
    project_manifest = json.loads(manifest_path.read_text())
    testables = project_manifest.pop('testables', [])
    if testables:
        write_json(manifest_path, project_manifest)
        write_json(output / 'project-manifest-adjustment.json', {
            'package_testables_removed_for_execute_method_fixture': testables,
            'legacy_package_archives_and_sources_changed': False,
        })
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
                audio.setnchannels(1)
                audio.setsampwidth(2)
                audio.setframerate(8000)
                audio.writeframes(b'\0\0' * 800)
        legacy_inputs = replace(args.project, legacy_packages, marker, args.legacy_ao_source,
                                expected, args.legacy_ao_production_only)
        phases = [phase(args, 'legacy', 'CreateLegacy', legacy_inputs, selected)]
    else:
        previous = json.loads((output / 'legacy-run.json').read_text())
        if not previous['passed'] or previous.get('scope') != args.scope:
            raise ValueError('The previous matching legacy phase did not pass')
        phases = [previous]
    if args.stop_after != 'legacy':
        if args.resume_from != 'migrated':
            current_inputs = replace(args.project, current_packages, marker, current=True)
            if args.cold_upgrade_import:
                cold_upgrade_import(args.project, output)
            phases.append(phase(args, 'broken', 'VerifyBroken', current_inputs, selected))
        else:
            previous = json.loads((output / 'broken-run.json').read_text())
            if not previous['passed'] or previous.get('scope') != args.scope:
                raise ValueError('The previous matching broken phase did not pass')
            current_inputs = previous['inputs']
            phases.append(previous)
        if args.stop_after == 'migrated':
            phases.append(phase(args, 'migrated', 'ApplyAndVerify', current_inputs, selected))
    summary = {
        'passed': True, 'scope': args.scope, 'owners': owners,
        'installation': 'Selected plugin ZIP updates only; migration engines ship inside each owner package',
        'operation': 'One explicitly invoked batch Apply per selected plugin',
        'fixture_sha256': sha(editor.read_bytes()),
        'holder_sha256': sha((args.project / 'Assets/ComponentUpgradeReferenceHolder.cs').read_bytes()),
        'config_sha256': sha(config_path.read_bytes()), 'phases': phases,
    }
    filename = 'three-stage-summary.json' if len(phases) == 3 else 'suite-state.json'
    write_json(output / filename, summary)
    print(json.dumps({'passed': True, 'scope': args.scope, 'owners': owners, 'output': str(output),
                      'phases': len(phases), 'paused': len(phases) < 3}, indent=2))


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Archive completed plugin-ZIP-only Unity upgrade consumers and their native byte proofs."""
import argparse
from datetime import datetime, timezone
import gzip
import hashlib
import json
import re
from pathlib import Path
import shutil
from zipfile import ZipFile

SCOPES = ('acc', 'ass', 'viseme', 'ao', 'all')
OWNERS = {
    'acc': ('top.sealoong.unitybox.advanced-costume-controller', 'AdvancedCostumeController'),
    'ass': ('top.sealoong.unitybox.avatar-security-system', 'AvatarSecuritySystem'),
    'viseme': ('top.sealoong.unitybox.viseme-blendshape-override', 'VisemeBlendshapeOverride'),
    'ao': ('top.sealoong.unitybox.animator-optimizer', 'AnimatorOptimizer'),
}
MAIN = {'Assets/ComponentUpgradeFixtures/' + value for value in ('Base.prefab', 'Variant.prefab', 'Nested.prefab', 'Avatar.unity')}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def dump(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def load(path):
    return json.loads(path.read_text())


def copy(src, dst):
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)


def gz(src, dst):
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(gzip.compress(src.read_bytes(), mtime=0))
    assert gzip.decompress(dst.read_bytes()) == src.read_bytes()


def index(root):
    dump(root / 'sha256.json', {str(path.relative_to(root)): sha(path)
         for path in sorted(root.rglob('*')) if path.is_file() and path != root / 'sha256.json'})


def verify_native_source(project, repo, native, archives):
    package, owner = native['package'], native['owner']
    assert native['engineType'] == 'UnityBox.' + owner + '.ComponentMigration.MigrationEngine'
    assert package == next(package for package, folder in OWNERS.values() if folder == owner)
    assert sha(Path(native['assemblyPath'])) == native['assemblySha256'], 'Native DLL was changed or discarded before archive'
    payload = archives[package]
    prefix = 'Packages/' + package + '/'
    matches = []
    for item in native['sourceFiles']:
        assert item['path'].startswith(prefix)
        relative = item['path'][len(prefix):]
        installed = project / item['path']
        checkout = repo / 'Assets/UnityBox' / owner / relative
        assert installed.read_bytes() == payload[relative] == checkout.read_bytes(), item['path']
        assert sha(installed) == item['sha256'], item['path']
        matches.append({'path': relative, 'sha256': item['sha256']})
    assert matches
    return {**native, 'source_matches_current_zip_and_checkout': True, 'verified_package_source_files': matches}


def duplicate_legacy_documents(project, state):
    proofs = []
    for record in state['duplicates']:
        old_guid = next(item['guid'] for item in state['types'] if item['type'] == record['type'])
        original = next(item for item in state['files'] if item['path'] == record['asset'])
        def document(data):
            docs = [part for part in re.split(rb'(?m)(?=^--- !u!)', data)
                    if b'guid: ' + old_guid.encode() in part and b'm_Script:' in part]
            assert len(docs) == 1, 'Expected one original missing-component document'
            return docs[0]
        before = document((project / original['original']).read_bytes())
        after_add = document((project / record['currentBytes']).read_bytes())
        final = document((project / record['asset']).read_bytes())
        assert before == after_add == final, 'Duplicate legacy component document changed'
        proofs.append({'owner': record['owner'], 'asset': record['asset'], 'legacy_guid': old_guid,
                       'original_component_document_sha256': hashlib.sha256(before).hexdigest(),
                       'bytes': len(before), 'unchanged_when_current_component_added_and_after_owner_batches': True})
    return proofs


def validate_evidence_joins(project, state, runs, current_inputs, archives):
    output = project / '.unitybox-component-upgrade'
    selected = {item['package'] for item in runs['migrated']['inputs']}
    frozen = {item['package']: item for item in current_inputs if item['package'] in selected}
    for phase, run in runs.items():
        assert run['result'] == load(output / (phase + '.json')), 'Phase result differs from its actual run record'
        if phase != 'legacy':
            actual = {item['package']: item for item in run['inputs']}
            assert set(actual) == set(frozen)
            for package, item in actual.items():
                assert item['archive_sha256'] == frozen[package]['sha256']
                assert item['version'] == frozen[package]['version']
    migrated = load(output / 'migrated.json')
    batches = migrated['batches']
    for previous, following in zip(batches, batches[1:]):
        assert {item['path']: item['sha256'] for item in previous['afterFiles']} == {item['path']: item['sha256'] for item in following['beforeFiles']}, 'Owner batch hash chain differs'
    final_hashes = {item['path']: item['sha256'] for item in batches[-1]['afterFiles']}
    assert set(final_hashes) == {item['path'] for item in state['files']}
    for item in state['files']:
        assert sha(project / item['path']) == final_hashes[item['path']], 'Final asset differs from native final hashes'
    for package in selected:
        for relative, expected in archives[package].items():
            assert (project / 'Packages' / package / relative).read_bytes() == expected, 'Installed full plugin payload differs from frozen ZIP: ' + package + '/' + relative
    return {'phase_results_equal_run_records': True, 'current_run_inputs_equal_frozen_plugin_zips': True,
            'installed_complete_plugin_payloads_equal_frozen_zips': True, 'owner_batch_hash_chain_continuous': True,
            'final_disk_files_equal_native_final_batch_hashes': True}


def archive_scope(scope, project, repo, work, destination, current_inputs, archives):
    output = project / '.unitybox-component-upgrade'
    state, created = load(output / 'legacy-state.json'), load(output / 'legacy-state-created.json')
    assert state['scope'] == created['scope'] == scope
    for name in ('types', 'files', 'components'):
        assert state[name] == created[name], 'Original legacy state was changed: ' + name
    selected = list(OWNERS.values()) if scope == 'all' else [OWNERS[scope]]
    owners = {owner for _, owner in selected}
    packages = {package for package, _ in selected}
    assert {item['owner'] for item in state['types']} == owners
    assert {item['owner'] for item in state['duplicates']} == owners
    suite = load(output / 'three-stage-summary.json')
    assert suite['passed'] and suite['scope'] == scope and set(suite['owners']) == owners
    runs = {}
    for phase in ('legacy', 'broken', 'migrated'):
        runs[phase] = load(output / (phase + '-run.json'))
        native = load(output / (phase + '.json'))
        assert runs[phase]['passed'] and runs[phase]['exit_code'] == 0 and native['passed'] and not native['error']
        assert set(runs[phase]['project_package_isolation']['installed_unitybox_packages']) == packages
        for key in ('fixture_source', 'holder_source'):
            record = runs[phase][key]
            actual = output / 'source-snapshots' / phase / Path(record['file']).name
            assert sha(actual) == record['sha256']
    migrated, broken = load(output / 'migrated.json'), load(output / 'broken.json')
    joins = validate_evidence_joins(project, state, runs, current_inputs, archives)
    assert broken['missingScripts'] == 7 * len(state['types'])
    assert migrated['repairedComponents'] == 2 * len(state['types'])
    assert migrated['inspectorTargets'] == 4 * len(state['types'])
    assert set(migrated['selectedAssets']) == MAIN
    assert set(migrated['readOnlyAssets']) == {item['asset'] for item in state['duplicates']}
    assert len(migrated['batches']) == len(owners)
    assert {batch['owner'] for batch in migrated['batches']} == owners
    typed = [component for component in state['components'] if component['asset'] in MAIN]
    metrics = {
        'components': len(typed),
        'properties': sum(len(component['properties']) for component in typed),
        'objectReferences': sum(prop['kind'] == 'ObjectReference' for component in typed for prop in component['properties']),
    }
    assert len(migrated['comparisonPasses']) == 2
    for result in migrated['comparisonPasses']:
        for key, value in metrics.items():
            assert result[key] == value, 'Native comparison metric differs: ' + key
    assert migrated['comparisonPasses'][0]['duplicateCurrentComponents'] == len(owners)
    for component in state['components']:
        parents = [prop['path'] for prop in component['properties'] if prop['kind'] == 'ObjectReference']
        assert not any(any(prop['path'].startswith(parent + '.') for parent in parents) for prop in component['properties'])
    for batch in migrated['batches']:
        own_types = {item['type'] for item in state['types'] if item['owner'] == batch['owner']}
        assert batch['applyInvocations'] == 1 and batch['repairedComponents'] == 2 * len(own_types)
        assert set(batch['readyTypes']) == own_types
        assert set(batch['changedAssets']) == {'Assets/ComponentUpgradeFixtures/Base.prefab', 'Assets/ComponentUpgradeFixtures/Avatar.unity'}
    if destination.exists():
        raise RuntimeError('Scope archive already exists; preserve it before rebuilding: ' + str(destination))
    destination.mkdir(parents=True)
    for name in ('config.json', 'legacy.json', 'broken.json', 'migrated.json', 'legacy-run.json', 'broken-run.json', 'migrated-run.json', 'legacy-state-created.json', 'legacy-state.json', 'three-stage-summary.json', 'cold-upgrade-import.json'):
        copy(output / name, destination / name)
    for folder in ('source-snapshots', 'generated-script-metadata'):
        shutil.copytree(output / folder, destination / folder)
    for item in state['types']:
        assert sha(output / 'generated-script-metadata' / (item['type'] + '.cs.meta')) == item['generatedMetaSha256']
    helper = runs['legacy']['fixture_helper_metadata']
    assert sha(Path(helper['file'])) == helper['sha256']
    assert sha(project / 'Assets/ComponentUpgradeReferenceHolder.cs.meta') == helper['sha256']
    legacy_snapshot = runs['legacy']['legacy_state_created']
    assert sha(output / 'legacy-state-created.json') == legacy_snapshot['sha256']
    for item in runs['legacy']['legacy_assemblies']['files']:
        assert sha(Path(runs['legacy']['legacy_assemblies']['directory']) / item['path']) == item['sha256']
    for item in state['files']:
        original = project / item['original']
        assert sha(original) == item['sha256'], item['path']
        gz(original, destination / 'original' / (item['path'] + '.gz'))
        gz(project / item['path'], destination / 'post' / (item['path'] + '.gz'))
    for duplicate in state['duplicates']:
        baseline = project / duplicate['currentBytes']
        assert baseline.read_bytes() == (project / duplicate['asset']).read_bytes()
        gz(baseline, destination / ('duplicate-before-apply-' + duplicate['owner'] + '.unity.gz'))
    for batch in migrated['batches']:
        before = {item['path']: item['sha256'] for item in batch['beforeFiles']}
        for path in batch['changedAssets']:
            backup = Path(batch['backupDirectory']) / path
            assert sha(backup) == before[path]
            gz(backup, destination / 'backup-original' / batch['owner'] / (path + '.gz'))
    fingerprints = [verify_native_source(project, repo, native, archives) for native in migrated['nativeOwners']]
    assert {item['owner'] for item in fingerprints} == owners
    marker = load(project / '.unitybox-test-project.json')
    dump(destination / 'input-provenance.json', {
        'scope': scope, 'legacy_inputs': runs['legacy']['inputs'],
        'current_plugin_zip_inputs': [item for item in current_inputs if item['package'] in packages],
        'unity_project_profile': marker['profile'],
        'unity_project_dependencies': {name: value for name, value in marker['packages'].items() if not name.startswith('top.sealoong.unitybox.')},
        'cold_upgrade_import': load(output / 'cold-upgrade-import.json'),
        'installation': 'Only the selected plugin ZIP updates; each migration API/source is inside its owner package.',
    })
    result = {
        'passed': True, 'scope': scope, 'owners': sorted(owners),
        'embedded_sources_match_current_zips_and_checkout': True, 'compiled_owner_engines': fingerprints,
        'native_evidence_joins': joins,
        'legacy_script_identities': len(state['types']), 'original_asset_and_meta_files': len(state['files']),
        'captured_component_records': len(state['components']),
        'captured_property_records': sum(len(component['properties']) for component in state['components']),
        'typed_component_records_compared_per_pass': metrics['components'],
        'persistent_field_comparisons_per_pass': metrics['properties'],
        'object_reference_global_ids_per_pass': metrics['objectReferences'],
        'read_only_duplicate_cases': len(state['duplicates']),
        'read_only_duplicate_properties_preserved_in_raw_bytes': sum(len(c['properties']) for c in state['components'] if c['asset'] not in MAIN),
        'transient_native_pptr_child_values_excluded': 0,
        'read_only_duplicate_legacy_documents': duplicate_legacy_documents(project, state),
        'explicit_owner_batch_calls': len(migrated['batches']), 'repaired_components': migrated['repairedComponents'],
        'inspector_targets': migrated['inspectorTargets'], 'missing_scripts_reproduced': broken['missingScripts'],
        'changed_assets': migrated['changedAssets'], 'current_state_retained': True,
    }
    dump(destination / 'final-source-and-state.json', result)
    index(destination)
    return result


def readme(results, inputs=None):
    lines = ['# 插件包内手动恢复的真实升级记录', '',
        '五个独立合成消费者工程覆盖 ACC、ASS、Viseme、AO 单独安装，以及四插件共存。每个当前阶段只更新对应插件 ZIP；手动恢复 API 随插件安装。', '',
        '| 消费者 | 类型数 | 复现 Missing | 显式批量调用 | 修复引用 | 每次属性/PPtr 核对 |',
        '| --- | ---: | ---: | ---: | ---: | ---: |']
    for scope in SCOPES:
        if scope in results:
            r = results[scope]
            lines.append(f"| [{scope}](Scopes/{scope}/three-stage-summary.json) | {r['legacy_script_identities']} | {r['missing_scripts_reproduced']} | {r['explicit_owner_batch_calls']} | {r['repaired_components']} | {r['persistent_field_comparisons_per_pass']} / {r['object_reference_global_ids_per_pass']} |")
    lines += ['', '第二阶段 `passed: true` 表示成功复现预期 Missing Script。第三阶段每个已安装插件显式执行一次批量 Scan→Apply；四插件共存时分别调用四个所属 API。各批仅处理自身类型，并核对其它插件的旧 GUID 字节保持。', '',
        '资源覆盖 Base Prefab、Variant、嵌套 Prefab、四组场景对象、实例覆盖、非默认配置和 Material/Renderer/Animator/AudioClip 引用。每个插件另有旧无效＋新有效同类组件案例，只读扫描后保持完整字节和两份组件。', '',
        '每批备份与该插件执行前的实际文件一致；后一插件的备份包含前面已完成的 GUID 修复。最终文件只改变确认的 `m_Script.guid` 跨度，资产 GUID、组件 GlobalObjectID、序列化 fileID/PPtr、字段、换行与其它字节保持。', '',
        '重复案例的旧 MonoBehaviour YAML 文档还逐字节对照最初创建、显式添加新组件后及最终状态，原配置文档完整保留。', '',
        '原字段在全部批次后和 Inspector OnEnable/延迟回调后各核对一次。ObjectReference 只记录父 GlobalObjectID；原 YAML 的 PPtr/fileID另做字节核对。实际数量由各 scope 的 `migrated.json.comparisonPasses` 给出。', '',
        '公开旧版为 ACC 0.4.13、ASS 0.7.24、Viseme 0.1.14 完整 ZIP，导入前核对 CRC/SHA256。AO 使用未发布开发提交 `782af0bf8569fdde6782d420761f38c8f8d6824d` 的精确 Runtime/Editor 源，仅排除旧开发 Tests；来源和排除列表完整记录。', '',
        '第一阶段即保存原状态、Unity 实际生成的脚本/helper metadata、旧 DLL 指纹和原资源字节。当前阶段仅重建专用工程 Library，并逐项核对原资产/meta 保持；冷导入检验实际磁盘 GUID。', '',
        '每个 `Scopes/<scope>/` 保存原生三阶段结果、命令、来源、原始创建状态、每阶段实际 fixture 源、生成 metadata、gzip 原文件/最终文件/逐插件备份及源码指纹。源码指纹同时匹配实际编译包、当前 ZIP 和 checkout。Editor/launcher 日志与原生 DLL 保留在专用测试工作区。', '',
        '## 复现', '',
        '先用仓库 `scripts/unity/test_project.py prepare --profile vrc` 准备专用消费者工程，`--package` 只选择该 scope 的插件；添加真实 SDK、MA、NDMF 依赖。然后执行本目录运行器：', '',
        '```bash',
        'python3 "$TASK_REPO/scripts/unity/test_project.py" prepare \\',
        '  --repo "$TASK_REPO" --project "$TASK_PROJECT" --profile vrc --offline \\',
        '  --dependency-cache "$TASK_DEPENDENCY_CACHE" --package AdvancedCostumeController \\',
        '  --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar', '',
        'python3 "$TASK_ARCHIVE/run_upgrade.py" --scope acc \\',
        '  --project "$TASK_PROJECT" --unity "$TASK_UNITY" --xdg-root "$TASK_XDG" \\',
        '  --legacy-package "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_OLD_ZIP" \\',
        '  --expected-legacy-sha "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_OLD_SHA" \\',
        '  --current-package "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_CURRENT_ZIP" \\',
        '  --cold-upgrade-import',
        '```', '',
        '其它单插件使用 `--scope ass`、`viseme`、`ao` 并替换对应输入；共存使用 `--scope all` 并提供全部四包。AO 添加 `--legacy-ao-source 782af0bf8569fdde6782d420761f38c8f8d6824d --legacy-ao-production-only`。旧 ZIP URL/SHA、当前 ZIP 身份及 AO 源证明见 [input-provenance.json](input-provenance.json)。', '',
        '可用 `--stop-after legacy` 暂停，再以同一 scope、当前 ZIP 输入和 `--resume-from broken --cold-upgrade-import` 继续。每阶段断言完成后自行退出。', '',
        '[summary.json](summary.json)汇总完成结果；[sha256.json](sha256.json)索引全部归档文件。', '']
    if inputs:
        versions = {item['package']: item['version'] for item in inputs}
        labels = {'acc': 'ACC', 'ass': 'ASS', 'viseme': 'Viseme', 'ao': 'AO'}
        lines.insert(4, '本轮当前包：' + '、'.join(labels[scope] + ' ' + versions[OWNERS[scope][0]] for scope in ('acc', 'ass', 'viseme', 'ao')) + '。')
        lines.insert(5, '')
    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', required=True, type=Path)
    parser.add_argument('--work', required=True, type=Path)
    parser.add_argument('--destination', required=True, type=Path)
    parser.add_argument('--current-inputs', required=True, type=Path)
    parser.add_argument('--project', action='append', required=True, metavar='SCOPE=OWNED_PROJECT')
    args = parser.parse_args()
    repo, work, destination = args.repo.resolve(), args.work.resolve(), args.destination.resolve()
    assert destination.is_relative_to(work), 'Build archive outside repository before publication'
    destination.mkdir(parents=True, exist_ok=True)
    inputs = load(args.current_inputs)
    archives = {}
    for item in inputs:
        source = Path(item['path'])
        assert sha(source) == item['sha256']
        with ZipFile(source) as archive:
            assert archive.testzip() is None
            archives[item['package']] = {info.filename: archive.read(info) for info in archive.infolist() if not info.is_dir()}
    provenance = {'public_and_development_legacy_inputs': load(work / 'legacy-inputs.json'),
        'ao_original_git_provenance': load(work / 'ao-legacy-782af0b/provenance.json'), 'current_plugin_zip_inputs': inputs}
    if (destination / 'input-provenance.json').exists():
        assert load(destination / 'input-provenance.json') == provenance, 'Current suite input freeze differs'
    dump(destination / 'input-provenance.json', provenance)
    for item in args.project:
        scope, separator, project = item.partition('=')
        assert separator and scope in SCOPES
        archive_scope(scope, Path(project).resolve(), repo, work, destination / 'Scopes' / scope, inputs, archives)
    for name in ('ComponentUpgradeFixture.cs', 'ComponentUpgradeReferenceHolder.cs', 'run_upgrade.py', 'archive_upgrade.py'):
        copy(work / name, destination / name)
    results = {scope: load(destination / 'Scopes' / scope / 'final-source-and-state.json') for scope in SCOPES
               if (destination / 'Scopes' / scope / 'final-source-and-state.json').is_file()}
    summary = {'passed': len(results) == len(SCOPES), 'suite_complete': len(results) == len(SCOPES),
        'captured_at_utc': datetime.now(timezone.utc).isoformat(), 'completed_scopes': list(results), 'expected_scopes': list(SCOPES),
        'installation': 'Plugin ZIP updates only; manual migration ships with each owner plugin', 'scopes': results}
    dump(destination / 'summary.json', summary)
    (destination / 'README.md').write_text(readme(results, inputs))
    index(destination)
    print(json.dumps({'archive': str(destination), 'completed_scopes': list(results), 'suite_complete': summary['suite_complete'],
                      'files': len([p for p in destination.rglob('*') if p.is_file()]), 'sha256_index': sha(destination / 'sha256.json')}, indent=2))


if __name__ == '__main__':
    main()

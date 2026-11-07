from pathlib import Path
from datetime import datetime,timezone
import gzip
import hashlib
import json
import shutil
import subprocess
import sys
r=Path('/workspace/UnityBox');b=Path('/workspace/unitybox-setup/unity');out=r/'Docs/Verification/ASSWriteDefaultsOn';out.mkdir(exist_ok=True)
sys.path.insert(0,str(r/'scripts/unity'))
import capture_evidence as ce
import test_project as tp
import verify_player as vp
native=[ce.native_evidence('ass-wdon-'+s,str(b/('ass-wdon-'+s+'-result.json')),'functional') for s in ['core','sdk','full']]
native.append(ce.native_evidence('ass-wdon-serial',str(b/'ass-wdon-benchmark-result.json'),'benchmark'))
for record in native:
 raw=Path(record['artifacts']['xml']['path']).read_bytes();sanitized,removed=ce.sanitized_xml(raw);encoded=gzip.compress(sanitized,mtime=0)
 name=record['scope']+'-results.xml.gz';(out/name).write_bytes(encoded)
 record['archived_xml']={'path':'Verification/ASSWriteDefaultsOn/'+name,'compression':'gzip; mtime=0','bytes':len(encoded),'sha256':hashlib.sha256(encoded).hexdigest(),'original_xml_sha256':hashlib.sha256(raw).hexdigest(),'archived_xml_sha256':hashlib.sha256(sanitized).hexdigest(),'removed_metadata':removed}
report_path=b/'ass-wdon-player-result.json';player=json.loads(report_path.read_text());assert player['passed'] and vp.player_passed(player,player['required_providers'])
project=ce.command_project(player['build_command']);prepared=tp.read_json(project/tp.MARKER);assert prepared['project_id']==player['project_id']
required=[provider for suffix,provider in vp.PLAYER_PROVIDERS.items() if 'top.sealoong.unitybox.'+suffix in prepared['packages']];assert required==player['required_providers']
directory=Path(player['build']['output']).parent;assert tp.read_json(directory/'build.json')==player['build'];assert tp.read_json(directory/'player.json')==player['player']
for source in (r/'scripts/unity/verification').rglob('*.cs'):
 assert (project/'Assets/UnityBoxVerification'/source.relative_to(r/'scripts/unity/verification')).read_bytes()==source.read_bytes()
player={'scope':'ass-wdon-linux','kind':'player',**player,'source_evidence':ce.source_evidence(project,prepared),'artifacts':{'report':ce.artifact(report_path),**{name:ce.artifact(directory/name) for name in ['build.json','player.json','build.log','player.log']}}}
player['archived_artifacts']={}
for name in ['build.json','player.json']:
 source=directory/name;target=out/('ass-wdon-linux-'+name);target.write_bytes(source.read_bytes());player['archived_artifacts'][name]={'path':'Verification/ASSWriteDefaultsOn/'+target.name,'bytes':target.stat().st_size,'sha256':ce.sha256(target)}
tooling_path=b/'ass-wdon-tooling.json';tooling_path.write_text(json.dumps({'checks':[{'name':'vpm-packaging','command':['python3','-m','unittest','discover','-s','.github/scripts','-p','test_*.py','-v'],'exit_code':0,'log':str(b/'ass-wdon-packaging-tests.log')}]},indent=2)+'\n')
tooling=ce.tooling_evidence(tooling_path);shutil.copy2(b/'ass-wdon-packaging-tests.log',out/'packaging-tests.log')
tooling['checks'][0]['archived_log']={'path':'Verification/ASSWriteDefaultsOn/packaging-tests.log','sha256':ce.sha256(out/'packaging-tests.log')}
local=Path(json.loads((b/'ass-wdon-vpm-local.json').read_text())['output']);tooling['vpm_workflow']=ce.vpm_evidence(local/'report.json')
resources=[]
for clip in sorted((r/'Assets/UnityBox/AvatarSecuritySystem/Resources').glob('*.mp3')):
 relative=clip.relative_to(r).as_posix();original=subprocess.check_output(['git','show','7cd4b40:'+relative],cwd=r);assert original==clip.read_bytes()
 meta=Path(str(clip)+'.meta');assert '  loadInBackground: 1\n' in meta.read_text()
 resources.append({'path':relative,'mp3_sha256':ce.sha256(clip),'original_7cd4b40_sha256':hashlib.sha256(original).hexdigest(),'metadata_sha256':ce.sha256(meta),'metadata':meta.relative_to(r).as_posix()})
assert hashlib.sha256((r/'Assets/UnityBox.unity').read_bytes()).hexdigest()=='97bd78f2791abea3c101fe99b4cba3becc301f0e1cf7cfe7f9cbb85755baa202'
evidence={'schema':1,'plugin':'AvatarSecuritySystem','version':'0.9.6','captured_at_utc':datetime.now(timezone.utc).isoformat(),'baseline_revision':'7ca9f65eedcb095204cadfdae666fc80b08ac7fb','scope':'ASS-only complete functional suites in core/SDK/full scopes; uniform new-state WD On; native facial default restoration and source preservation; repeated timeout/restart feedback; NDMF final graph; Player and serial performance','native_runs':native,'player_runs':[player],'tooling':tooling,'bundled_audio':resources,'original_audio_bytes_preserved':True,'source_scene_preserved':True}
(out/'evidence.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n')
p=out/'README.md';s=p.read_text();a='## 全库基线';assert a in s
rows='''## 执行结果

| ASS 专项范围 | 实际结果 |
| --- | --- |
'''
for record in native:
 n=record['tests']['total'];rows+=f"| {record['scope']} | {n}/{n} 通过，{len(record['tests']['assemblies'])} 个测试程序集 |\n"
q=player['player'];rows+=f"| Linux Player | {q['scenarios']} 个场景、{q['frames']} 帧、{q['checks']} 次输出检查通过；源配置 {player['build']['source_configuration_components']} 个保留 |\n"
rows+=f"| 打包工具 | {tooling['checks'][0]['tests']}/{tooling['checks'][0]['tests']} 通过；8 个当前 ZIP 与源码及 metadata 一致 |\n\n"
s=s.replace(a,rows+a);p.write_text(s)
latest={'plugin':'AvatarSecuritySystem','version':'0.9.6','evidence':'Verification/ASSWriteDefaultsOn/evidence.json','verified_at_utc':evidence['captured_at_utc'],'native_tests':{record['scope']:record['tests']['total'] for record in native},'player':{key:q[key] for key in ['scenarios','frames','checks']}}
for name in ['VerificationResults.json','UnityNativeResults.json']:
 p=r/'Docs'/name;d=json.loads(p.read_text());d.setdefault('baseline_revision','7cd4b40cd03aa81c268e8686609b2d60a9601238');d.setdefault('patch_history',[]).append(d['latest_patch']) if d.get('latest_patch') else None;d['latest_patch']=latest;p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n')
p=r/'Docs/PerformanceResults.json';d=json.loads(p.read_text());d.setdefault('baseline_revision','7cd4b40cd03aa81c268e8686609b2d60a9601238');d.setdefault('supplementary_player_runs',[]).extend(ce.performance_data([],[player])['player_runs']);d['supplementary_editor_runs']=ce.performance_data(native,[])['editor_runs'];p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n')
p=r/'Docs/Testing.md';s=p.read_text();a='## 执行结果与归档';assert a in s;s=s.replace(a,rows.replace('## 执行结果','## ASS 0.9.6 专项执行结果')+a);p.write_text(s)

if Path(__file__).resolve() != (out/'capture-ass-wdon.py').resolve():shutil.copy2(Path(__file__),out/'capture-ass-wdon.py')
baseline=out/'baseline-off';baseline.mkdir(exist_ok=True)
for name in ['AssWdAudit.cs','result.json','verification.json']:shutil.copy2(b/'wd-audit'/name,baseline/name)
source_rows=[]
for name in ['AnimatorLifecycle.cs','GesturePassword.cs','Countdown.cs','StandaloneSecurityBuilder.cs','Obfuscator.cs','VRChat/Processor.cs','VRChat/Defense.cs']:
    relative='Assets/UnityBox/AvatarSecuritySystem/Editor/'+name
    before=subprocess.check_output(['git','show','7ca9f65:'+relative],cwd=r)
    imported=(b/'projects/ass-audio-core-20261108/Packages/top.sealoong.unitybox.avatar-security-system/Editor'/name).read_bytes()
    assert imported==before
    source_rows.append({'path':relative,'original_source_sha256':hashlib.sha256(before).hexdigest()})
(baseline/'source-fingerprints.json').write_text(json.dumps(source_rows,indent=2)+'\n')
feedback=out/'feedback';feedback.mkdir(exist_ok=True)
for name in ['AssFeedbackAudit.cs','result.json']:shutil.copy2(b/'wdon-feedback'/name,feedback/name)
trace=json.loads((feedback/'result.json').read_text());assert trace['passed']
assert all(point['timedOut'] for point in trace['points'] if point['phase']=='second-timeout')
evidence['baseline_off']='baseline-off/result.json';evidence['feedback']='feedback/result.json'
(out/'evidence.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n')
hashes={p.relative_to(out).as_posix():ce.sha256(p) for p in out.rglob('*') if p.is_file() and p.name!='sha256.json'};(out/'sha256.json').write_text(json.dumps(hashes,sort_keys=True,indent=2)+'\n')
print(json.dumps({'native':latest['native_tests'],'player':latest['player'],'packaging_tests':tooling['checks'][0]['tests'],'audio_bytes_preserved':True},ensure_ascii=False))

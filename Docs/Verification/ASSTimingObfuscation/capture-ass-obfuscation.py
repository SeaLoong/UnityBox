from pathlib import Path
from datetime import datetime, timezone
import gzip, hashlib, json, shutil, subprocess, sys, re
r=Path('/workspace/UnityBox'); b=Path('/workspace/unitybox-setup/unity'); out=r/'Docs/Verification/ASSTimingObfuscation'; out.mkdir(exist_ok=True)
sys.path.insert(0,str(r/'scripts/unity'))
import capture_evidence as ce
import test_project as tp
import verify_player as vp
baseline='c4a4e2614178b2918fb7b1027554f91ebb34c936'
native=[ce.native_evidence('ass-obfuscation-'+s,str(b/('ass-obfuscation-'+s+'-result.json')),'functional') for s in ['core','sdk','full']]
native += [ce.native_evidence('ass-obfuscation-graphics',str(b/'ass-obfuscation-graphics-result.json'),'graphics'), ce.native_evidence('ass-obfuscation-serial',str(b/'ass-obfuscation-benchmark-result.json'),'benchmark')]
for record in native:
 raw=Path(record['artifacts']['xml']['path']).read_bytes(); sanitized,removed=ce.sanitized_xml(raw); encoded=gzip.compress(sanitized,mtime=0)
 name=record['scope']+'-results.xml.gz'; (out/name).write_bytes(encoded)
 record['archived_xml']={'path':'Verification/ASSTimingObfuscation/'+name,'compression':'gzip; mtime=0','bytes':len(encoded),'sha256':hashlib.sha256(encoded).hexdigest(),'original_xml_sha256':hashlib.sha256(raw).hexdigest(),'archived_xml_sha256':hashlib.sha256(sanitized).hexdigest(),'removed_metadata':removed}
report_path=b/'ass-obfuscation-player-result.json'; player=json.loads(report_path.read_text()); assert player['passed'] and vp.player_passed(player,player['required_providers'])
project=ce.command_project(player['build_command']); prepared=tp.read_json(project/tp.MARKER); assert prepared['project_id']==player['project_id']
required=[provider for suffix,provider in vp.PLAYER_PROVIDERS.items() if 'top.sealoong.unitybox.'+suffix in prepared['packages']]; assert required==player['required_providers']
directory=Path(player['build']['output']).parent; assert tp.read_json(directory/'build.json')==player['build']; assert tp.read_json(directory/'player.json')==player['player']
for source in (r/'scripts/unity/verification').rglob('*.cs'):
 assert (project/'Assets/UnityBoxVerification'/source.relative_to(r/'scripts/unity/verification')).read_bytes()==source.read_bytes()
player={'scope':'ass-obfuscation-linux','kind':'player',**player,'source_evidence':ce.source_evidence(project,prepared),'artifacts':{'report':ce.artifact(report_path),**{name:ce.artifact(directory/name) for name in ['build.json','player.json','build.log','player.log']}}}
player['archived_artifacts']={}
for name in ['build.json','player.json']:
 target=out/('ass-obfuscation-linux-'+name); target.write_bytes((directory/name).read_bytes()); player['archived_artifacts'][name]={'path':'Verification/ASSTimingObfuscation/'+target.name,'bytes':target.stat().st_size,'sha256':ce.sha256(target)}
tooling_path=b/'ass-obfuscation-tooling.json'; tooling_path.write_text(json.dumps({'checks':[{'name':'vpm-packaging','command':['python3','-m','unittest','discover','-s','.github/scripts','-p','test_*.py','-v'],'exit_code':0,'log':str(b/'ass-obfuscation-packaging-tests.log')}]},indent=2)+'\n')
tooling=ce.tooling_evidence(tooling_path); shutil.copy2(b/'ass-obfuscation-packaging-tests.log',out/'packaging-tests.log'); tooling['checks'][0]['archived_log']={'path':'Verification/ASSTimingObfuscation/packaging-tests.log','sha256':ce.sha256(out/'packaging-tests.log')}
local=Path(json.loads((b/'ass-obfuscation-vpm-local.json').read_text())['output']); tooling['vpm_workflow']=ce.vpm_evidence(local/'report.json')
resources=[]
for clip in sorted((r/'Assets/UnityBox/AvatarSecuritySystem/Resources').glob('*.mp3')):
 rel=clip.relative_to(r).as_posix(); original=subprocess.check_output(['git','show',baseline+':'+rel],cwd=r); assert original==clip.read_bytes()
 meta=Path(str(clip)+'.meta'); assert subprocess.check_output(['git','show',baseline+':'+meta.relative_to(r).as_posix()],cwd=r)==meta.read_bytes()
 resources.append({'path':rel,'mp3_sha256':ce.sha256(clip),'metadata_sha256':ce.sha256(meta),'unchanged_since':baseline})
paths=subprocess.check_output(['git','ls-tree','-r','--name-only',baseline],cwd=r,text=True).splitlines()
runtime=[p for p in paths if p.startswith('Assets/UnityBox/') and '/Runtime/' in p and p.endswith('.meta')]
assert len(runtime)==16
for p in runtime: assert subprocess.check_output(['git','show',baseline+':'+p],cwd=r)==(r/p).read_bytes()
assert ce.sha256(r/'Assets/UnityBox.unity')=='97bd78f2791abea3c101fe99b4cba3becc301f0e1cf7cfe7f9cbb85755baa202'
other=[p for p in paths if p.startswith('Assets/UnityBox/') and not p.startswith('Assets/UnityBox/AvatarSecuritySystem/')]
for p in other: assert subprocess.check_output(['git','show',baseline+':'+p],cwd=r)==(r/p).read_bytes()
obf='Assets/UnityBox/AvatarSecuritySystem/Editor/Obfuscator.cs'; old=subprocess.check_output(['git','show',baseline+':'+obf],cwd=r); current=(r/obf).read_bytes()
assert old.replace(b'bool useInstructional = (PseudoInt(ref rng, 0, 2) == 0)',b'bool useInstructional = (i == 0 || PseudoInt(ref rng, 0, 2) == 0)')==current
word_pool={'unchanged_since':baseline,'comparison':'Full Obfuscator.cs equals baseline except first fake state always selecting an instructional clip; all word pools and Clip/State naming maps unchanged.','baseline_obfuscator_sha256':hashlib.sha256(old).hexdigest(),'current_obfuscator_sha256':ce.sha256(r/obf)}
evidence={'schema':1,'plugin':'AvatarSecuritySystem','version':'0.9.7','captured_at_utc':datetime.now(timezone.utc).isoformat(),'baseline_revision':baseline,'scope':'ASS-only complete functional suites, actual Inspector graphics, saved graph/curve reload, SDK late callback Contact controls, NDMF Finish plus late callback, Player and serial performance; existing word pools retained','native_runs':native,'player_runs':[player],'tooling':tooling,'bundled_audio':resources,'word_pool':word_pool,'source_scene_preserved':True,'runtime_metadata_preserved':len(runtime),'other_seven_plugins_unchanged':True}
(out/'evidence.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n')
rows='## 执行结果\n\n| ASS 专项范围 | 实际结果 |\n| --- | --- |\n'
for record in native:
 n=record['tests']['total']; rows+=f"| {record['scope']} | {n}/{n} 通过，{len(record['tests']['assemblies'])} 个测试程序集 |\n"
q=player['player']; rows+=f"| Linux Player | {q['scenarios']} 个场景、{q['frames']} 帧、{q['checks']} 次输出检查通过；源配置 {player['build']['source_configuration_components']} 个保留 |\n"
rows+=f"| 打包工具 | {tooling['checks'][0]['tests']}/{tooling['checks'][0]['tests']} 通过；8 个当前 ZIP 与源码及 metadata 一致 |\n\n"
p=out/'README.md'; s=p.read_text(); s=re.sub(r'## 执行结果\n.*?(?=## 历史快照)', '', s, flags=re.S); s=s.replace('## 历史快照',rows+'## 历史快照'); p.write_text(s)
latest={'plugin':'AvatarSecuritySystem','version':'0.9.7','evidence':'Verification/ASSTimingObfuscation/evidence.json','verified_at_utc':evidence['captured_at_utc'],'native_tests':{record['scope']:record['tests']['total'] for record in native},'player':{key:q[key] for key in ['scenarios','frames','checks']}}
for name in ['VerificationResults.json','UnityNativeResults.json']:
 p=r/'Docs'/name; d=json.loads(p.read_text()); d.setdefault('patch_history',[]); d['patch_history'] += [d['latest_patch']] if d['latest_patch']['version'] != '0.9.7' else []; d['latest_patch']=latest; p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n')
perf=ce.performance_data(native,[player]); p=r/'Docs/PerformanceResults.json'; d=json.loads(p.read_text())
for key in ['editor_runs','graphics_runs','player_runs']:
 d['supplementary_'+key]=[run for run in d.get('supplementary_'+key,[]) if not run['scope'].startswith('ass-obfuscation-')]+perf[key]
p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n')
p=r/'Docs/Testing.md'; s=p.read_text(); s=re.sub(r'## ASS 0.9.7 专项执行结果\n.*?(?=## ASS 0.9.6 WD On 与表情复位)', '', s, flags=re.S); s=s.replace('## ASS 0.9.6 WD On 与表情复位',rows.replace('## 执行结果','## ASS 0.9.7 专项执行结果')+'## ASS 0.9.6 WD On 与表情复位'); p.write_text(s)
p=r/'Docs/Performance.md'; s=p.read_text(); s=re.sub(r'本版独立串行性能范围.*?(?=ASS0.9.6 的 WD On)', '', s, flags=re.S); anchor='ASS0.9.6 的 WD On、该版串行基准与 Player 结果见'; assert anchor in s
ms=[m for run in perf['editor_runs'] for m in run['measurements']]; selected=[m for m in ms if m['scenario'].startswith('animator_idle_')]; assert len(selected)==2
text=f"本版独立串行性能范围通过{native[-1]['tests']['total']}项测试，记录{len(ms)}条测量。\n诱饵对照使用同一 ASS 图、固定远端输入，预热30次，9组×300次 Animator.Update；\n每组核对 Light 输出与默认状态，源资源保持。\n\n| Animator.Update 对照 | 层数 | 状态数 | 中位数 ms | p95 ms |\n| --- | ---: | ---: | ---: | ---: |\n"
for m in selected:
 text+=f"| {m['scenario']} | {m['animator_layers']} | {m.get('states',m.get('state_count','见JSON'))} | {m['median_ms']:.6f} | {m['p95_ms']:.6f} |\n"
text+='\n这些数据对应上述固定图和输入，启用诱饵会增加图规模；其它 Avatar 的耗时随层数、状态和动画变化。\n\n'
s=s.replace(anchor,text+anchor); p.write_text(s)
if Path(__file__).resolve() != (out/Path(__file__).name).resolve(): shutil.copy2(Path(__file__),out/Path(__file__).name)
(out/'sha256.json').write_text(json.dumps({p.relative_to(out).as_posix():ce.sha256(p) for p in out.rglob('*') if p.is_file() and p.name!='sha256.json'},sort_keys=True,indent=2)+'\n')
print(json.dumps({'native':latest['native_tests'],'player':latest['player'],'measurements':selected,'packaging':tooling['checks'][0]['tests']},ensure_ascii=False))

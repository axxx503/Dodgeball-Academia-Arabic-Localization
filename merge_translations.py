"""Atomic, repeatable merge. Reviewed strings are source-bound and never auto-overwritten."""
from pathlib import Path
import json,hashlib
from markup import validate,parse,signature
R=Path(__file__).resolve().parent
catalog=json.loads((R/'text-catalog.json').read_text('utf8'))
translations={}
for f in sorted((R/'translations').glob('*-ar.json')):
    for k,v in json.loads(f.read_text('utf8')).items():
        if k in translations:raise ValueError(f'duplicate translation key {k}')
        translations[k]=v
by_id={}
for r in catalog:by_id.setdefault(r['id'],[]).append(r)
errors=[];applied=0;unchanged=0
bindfile=R/'translation-source-bindings.json'
bindings=json.loads(bindfile.read_text('utf8')) if bindfile.exists() else {}
new_bindings=dict(bindings)
for k,t in translations.items():
    targets=[r for r in catalog if r['key']==k] if ':' in k else by_id.get(k,[])
    if len(targets)!=1:
        errors.append({'id':k,'error':f'expected one source, found {len(targets)}'});continue
    r=targets[0]
    digest=hashlib.sha256(r['en'].encode('utf8')).hexdigest()
    binding=dict(key=r['key'],source_sha256=digest)
    if k in bindings and bindings[k]!=binding:
        errors.append({'id':k,'error':'source/row changed since translation was bound; review required'});continue
    try:issues=validate(r['en'],t)
    except ValueError as e:issues=[str(e)]
    if issues:errors.append({'key':r['key'],'id':k,'issues':issues});continue
    r.update(ar=t,translation_status='authored_pending_visual_review',source_sha256=digest)
    new_bindings[k]=binding
    applied+=1
    unchanged+=int(t==r['en'])
report=dict(records=len(catalog),authored_records=applied,deliberately_unchanged=unchanged,untranslated=len(catalog)-applied,errors=errors,runtime_verified=False)
(R/'translation-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf8')
if errors:
    print(json.dumps(report,ensure_ascii=False));raise SystemExit(1)
p=R/'translated-catalog.json';tmp=p.with_suffix('.tmp')
tmp.write_text(json.dumps(catalog,ensure_ascii=False,indent=2),'utf8');tmp.replace(p)
tmp=bindfile.with_suffix('.tmp');tmp.write_text(json.dumps(new_bindings,ensure_ascii=False,indent=2),'utf8');tmp.replace(bindfile)
print(json.dumps(report,ensure_ascii=False))

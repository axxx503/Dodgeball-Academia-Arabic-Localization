from pathlib import Path
import sys,json,hashlib
from collections import Counter
R=Path(__file__).resolve().parent
# Install declared Python dependencies in your environment.
import UnityPy
import os
G=Path(os.environ['ACADEMIA_ORIGINAL_GAME'])/'DodgeballAcademia_Data'
targets=[G/'resources.assets',G/'globalgamemanagers.assets']
targets+=list((G/'StreamingAssets/AssetBundles').glob('fontsmeta_*'))
targets=[p for p in targets if p.is_file() and not p.name.endswith('.manifest')]
out=R/'extracted';out.mkdir(parents=True,exist_ok=True)
report=[]
for p in targets:
    env=UnityPy.load(str(p));types=Counter();objects=[];errors=[]
    for obj in env.objects:
        types[obj.type.name]+=1
        if obj.type.name not in ('TextAsset','Font','MonoBehaviour','MonoScript'):continue
        try:
            tree=obj.read_typetree()
            name=tree.get('m_Name','');entry=dict(path_id=obj.path_id,type=obj.type.name,name=name,fields=list(tree)[:25])
            if obj.type.name=='MonoScript':entry.update(class_name=tree.get('m_ClassName'),namespace=tree.get('m_Namespace'))
            if obj.type.name=='TextAsset':
                data=obj.read();text=data.m_Script
                if isinstance(text,bytes):text=text.decode('utf8','replace')
                entry.update(length=len(text),preview=text[:160])
                (out/(p.name+'.'+str(obj.path_id)+'.txt')).write_text(text,'utf8')
            if obj.type.name=='MonoBehaviour' and any(x in str(tree.keys()).lower() for x in ('language','translation','localization','term','font','dialog')):
                (out/(p.name+'.'+str(obj.path_id)+'.json')).write_text(json.dumps(tree,ensure_ascii=False,indent=2,default=str),'utf8')
                entry['tree_exported']=True
            objects.append(entry)
        except Exception as e:errors.append(dict(path_id=obj.path_id,type=obj.type.name,error=str(e)[:180]))
    report.append(dict(file=str(p),sha256=hashlib.file_digest(p.open('rb'),'sha256').hexdigest(),unity_versions=list({getattr(f,'unity_version','') for f in env.files.values()}),types=dict(types),objects=objects,errors=errors))
(R/'asset-inventory.json').write_text(json.dumps(report,ensure_ascii=False,indent=2,default=str),'utf8')
print(json.dumps([dict(file=Path(r['file']).name,types=r['types'],objects=len(r['objects']),errors=len(r['errors'])) for r in report],indent=2))

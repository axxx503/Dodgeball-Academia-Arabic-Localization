"""Lossless source-verified resource injection in workspace only.
This internal engineering prototype contains logical Arabic in the English
column; contextual shaping/BiDi integration and runtime tests are still pending.
Never package or install this internal full resource file as a release.
"""
from pathlib import Path
import json,sys,hashlib
from markup import validate
R=Path(__file__).resolve().parent
# Install declared Python dependencies in your environment.
import UnityPy
from revision_inputs import original_file
SRC=original_file('DodgeballAcademia_Data/resources.assets')
OUT=R/'build/internal';OUT.mkdir(parents=True,exist_ok=True)
inventory=json.loads((R/'asset-inventory.json').read_text('utf8'))
expected_source=inventory[0]['sha256'];original_sha=hashlib.sha256(SRC.read_bytes()).hexdigest()
if original_sha!=expected_source:raise SystemExit('Installed resource differs from the inspected source; refusing injection.')
report=json.loads((R/'extraction-report.json').read_text('utf8'))
catalog_bytes=(R/'translated-catalog.json').read_bytes()
catalog_sha=hashlib.sha256(catalog_bytes).hexdigest()
catalog=json.loads(catalog_bytes.decode('utf8'))
author_inputs={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((R/'translations').glob('*-ar.json'))}
bindings_sha=hashlib.sha256((R/'translation-source-bindings.json').read_bytes()).hexdigest()
bindings=json.loads((R/'translation-source-bindings.json').read_text('utf8'))
catalog_by_key={r['key']:r for r in catalog}
authored_keys=set()
for p in sorted((R/'translations').glob('*-ar.json')):
    for author_key,arabic in json.loads(p.read_text('utf8')).items():
        binding=bindings.get(author_key)
        if binding is None:raise RuntimeError('Author text has no verified source binding: '+author_key)
        row=catalog_by_key.get(binding['key'])
        if row is None or row['key'] in authored_keys:raise RuntimeError('Missing/duplicate authored catalog record: '+author_key)
        if row['ar']!=arabic or row['source_sha256']!=binding['source_sha256']:
            raise RuntimeError('Author text and merged catalog differ: '+author_key)
        authored_keys.add(row['key'])
if authored_keys!={r['key'] for r in catalog if r['ar']}:raise RuntimeError('Merged catalog author coverage differs')
translations={r['key']:r for r in catalog if r['ar']}
env=UnityPy.load(str(SRC));objects={o.path_id:o for o in env.objects}
raw_before={k:o.get_raw_data() for k,o in objects.items()}
expected_scripts={};changed=[];total=0
for table in report['tables']:
    pid=table['path_id'];obj=objects[pid];data=obj.read();text=data.m_Script
    assert hashlib.sha256(text.encode('utf8')).hexdigest()==table['raw_sha256']
    lines=text.split('\r\n');rows=[line.split('\t') for line in lines]
    col=table['header'].index('en_US');count=0
    for key,r in translations.items():
        if r['path_id']!=pid:continue
        row=r['row']-1
        assert rows[row][col]==r['en'],(key,'source cell mismatch')
        assert hashlib.sha256(r['en'].encode('utf8')).hexdigest()==r['source_sha256']
        assert not validate(r['en'],r['ar']),(key,'invalid commands')
        if r['en']==r['ar']:continue
        cells=rows[row].copy();cells[col]=r['ar'];lines[row]='\t'.join(cells);count+=1
    if count:
        updated='\r\n'.join(lines)
        # Regression guard from native-slot review: prose can occur in a code
        # cell and a populated locale is not sufficient permission to inject.
        for excluded in report['excluded_unkeyed']:
            if excluded['path_id']==pid:
                n=excluded['row']-1
                assert lines[n]==text.split('\r\n')[n],(pid,n+1,'excluded native row changed')
        # Compare every row/cell. Only explicitly authored English cells may change.
        after=[line.split('\t') for line in updated.split('\r\n')]
        assert len(after)==len(rows)
        for n,(a,b) in enumerate(zip(rows,after),1):
            assert len(a)==len(b)
            for j,(left,right) in enumerate(zip(a,b)):
                if left!=right:
                    assert j==col and f'{pid}:{n}' in translations
                    assert right==translations[f'{pid}:{n}']['ar']
        expected_scripts[pid]=updated;data.m_Script=updated;data.save();changed.append(pid);total+=count
candidate=OUT/'resources.assets';candidate.write_bytes(env.file.save())
reopened=UnityPy.load(str(candidate));after_objects={o.path_id:o for o in reopened.objects}
assert set(objects)==set(after_objects),'object IDs changed'
for pid,o in after_objects.items():
    if pid in expected_scripts:assert o.read().m_Script==expected_scripts[pid]
    else:assert o.get_raw_data()==raw_before[pid],(pid,'untouched object bytes changed')
assert hashlib.sha256(SRC.read_bytes()).hexdigest()==original_sha,'original unexpectedly changed'
assert hashlib.sha256((R/'translated-catalog.json').read_bytes()).hexdigest()==catalog_sha,'catalog changed during build'
assert author_inputs=={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((R/'translations').glob('*-ar.json'))},'author files changed during build'
assert hashlib.sha256((R/'translation-source-bindings.json').read_bytes()).hexdigest()==bindings_sha,'source bindings changed during build'
result=dict(status='internal engineering prototype; NOT a release',catalog_sha256=catalog_sha,author_inputs_sha256=author_inputs,source_bindings_sha256=bindings_sha,source_sha256=original_sha,prototype_sha256=hashlib.sha256(candidate.read_bytes()).hexdigest(),translated_changed_cells=total,changed_textassets=changed,object_count=len(objects),roundtrip_all_changed_textassets=True,unchanged_objects_byte_verified=len(objects)-len(changed),other_columns_and_unkeyed_rows_unchanged=True,excluded_native_rows_preserved=len(report['excluded_unkeyed']),language_slot='en_US',text_order='logical Arabic; renderer integration pending',runtime_verified=False,installed=False,ready_for_release=False)
(OUT/'text-build-report.json').write_text(json.dumps(result,indent=2),'utf8')
print(json.dumps(result))

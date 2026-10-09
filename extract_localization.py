from pathlib import Path
import json,re,sys,hashlib
R=Path(__file__).resolve().parent
inventory=json.loads((R/'asset-inventory.json').read_text('utf8'))
records=[];tables=[];warnings=[];excluded=[]
# Install declared Python dependencies in your environment.
import UnityPy
from revision_inputs import original_file
env=UnityPy.load(str(original_file('DodgeballAcademia_Data/resources.assets')))
objects={obj.path_id:obj for obj in env.objects if obj.type.name=='TextAsset'}
for item in inventory[0]['objects']:
    if item['type']!='TextAsset':continue
    p=R/'extracted'/('resources.assets.'+str(item['path_id'])+'.txt')
    # Raw game TSV uses CRLF between records, bare CR INSIDE cells. Do not
    # universal-newline normalize it or use csv.reader which splits bare CR.
    text=objects[item['path_id']].read().m_Script
    if 'en_US' not in text.split('\r\n',1)[0].split('\t'):continue
    p.write_bytes(text.encode('utf8'))
    rows=[line.split('\t') for line in text.split('\r\n')]
    header=rows[0];english=header.index('en_US');count=0
    id_columns=[i for i,h in enumerate(header) if h.endswith(' ID')]
    if len(id_columns)!=1:
        warnings.append(dict(table=item['name'],reason='unclassified identifier column',header=header));continue
    id_column=id_columns[0];parent_id='';block=0;group_ordinal=3
    grouped=header[id_column] in ('Ability ID','Item ID','Equip ID')
    for rownum,row in enumerate(rows[1:],2):
        if not row:continue
        if len(row)>len(header):warnings.append(dict(table=item['name'],row=rownum,reason='too many columns',columns=len(row)));continue
        row+=['']*(len(header)-len(row))
        en=row[english]
        identifier=row[id_column].strip()
        if identifier:parent_id=identifier;block+=1;group_ordinal=0
        # Native ExtractTSV consumes exactly three positional grouped rows:
        # name, description, code; extra blank-ID rows are ignored. The ordinal
        # advances even if the English cell is empty. Other-locale presence is
        # NOT a reliable role test (removed ability text occupies a code slot).
        native_role=None
        if grouped and group_ordinal<3:
            native_role=('name','description','code')[group_ordinal]
            group_ordinal+=1
        if not en:continue
        field='text'
        if grouped:
            if native_role not in ('name','description'):
                excluded.append(dict(table=item['name'],path_id=item['path_id'],row=rownum,en=en,
                    native_role=native_role,reason='native code slot or extra grouped draft; preserve unchanged'))
                continue
            identifier=parent_id;field=native_role
        elif not identifier:
            excluded.append(dict(table=item['name'],path_id=item['path_id'],row=rownum,en=en,
                reason='empty String ID skipped by native ExtractTSV; preserve unchanged'))
            continue
        record=dict(key=f"{item['path_id']}:{rownum}",id=identifier,block=block,field=field,table=item['name'],path_id=item['path_id'],row=rownum,en=en,ar='',comments=row[header.index('Comments')] if 'Comments' in header else '',speaker=row[header.index('Speaker')] if 'Speaker' in header else '',tags=re.findall(r'\[[^\]]*\]',en))
        records.append(record);count+=1
    tables.append(dict(table=item['name'],path_id=item['path_id'],header=header,english_records=count,raw_sha256=hashlib.sha256(text.encode('utf8')).hexdigest()))
seen={};duplicates=[]
for r in records:
    if r['id'] in seen:duplicates.append(dict(id=r['id'],table=r['table'],previous_table=seen[r['id']]['table'],same_english=r['en']==seen[r['id']]['en']))
    else:seen[r['id']]=r
(R/'text-catalog.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),'utf8')
(R/'extraction-report.json').write_text(json.dumps(dict(tables=tables,records=len(records),unique_ids=len(seen),duplicates=duplicates,warnings=warnings,excluded_unkeyed=excluded,runtime_verified=False),ensure_ascii=False,indent=2),'utf8')
print(json.dumps(dict(tables=len(tables),records=len(records),unique_ids=len(seen),duplicates=len(duplicates),warnings=len(warnings))))

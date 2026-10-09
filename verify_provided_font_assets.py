"""Verify new USER font bundles on files only. Never launch or touch saves."""
from pathlib import Path
import hashlib,json,sys,subprocess
R=Path(__file__).resolve().parent
# Install declared Python dependencies in your environment.
import UnityPy,arabic_reshaper
from fontTools.ttLib import TTFont
from PIL import Image,ImageDraw,ImageFont
from markup import parse,visible_text
from revision_inputs import PROVIDED,FONT_DIR,BUNDLES,font_file,original_file
assert PROVIDED
def sha(path):
    with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
catalog=json.loads((R/'translated-catalog.json').read_text('utf8'))
inline=json.loads((R/'review/inline-dialogue-draft-20261008.json').read_text('utf8'))['rows']
credits=json.loads((R/'review/credits-role-draft-20261008.json').read_text('utf8'))['rows']
texts=[visible_text(parse(x['ar'])) for x in catalog if x['ar']]
texts += [visible_text(parse(x['ar'])) for x in inline+credits]
shaper=arabic_reshaper.ArabicReshaper(configuration={'delete_harakat':False,'support_ligatures':False})
required={ord(c) for t in texts for c in t+shaper.reshape(t) if not c.isspace() and c!='|'}
build=json.loads((BUNDLES/'atlas-build-report.json').read_text('utf8'))
checks=[]
for weight,family in [('Regular','bekind'),('Bold','questrian')]:
    f=font_file(weight);cmap=TTFont(f).getBestCmap()
    assert not required-set(cmap),'Missing required characters in supplied font'
    prefix='DodgeballAcademia_Data/StreamingAssets/AssetBundles/'
    src=original_file(prefix+'fonts_'+family)
    source=UnityPy.load(str(src));candidate=UnityPy.load(str(BUNDLES/('fonts_'+family)))
    before={o.path_id:o for o in source.objects};after={o.path_id:o for o in candidate.objects}
    assert before.keys()==after.keys()
    textures=[i for i,o in before.items() if o.type.name=='Texture2D'];assert len(textures)==1
    tid=textures[0]
    assert all(o.get_raw_data()==after[i].get_raw_data() for i,o in before.items() if i!=tid)
    original=before[tid].read().image;atlas=after[tid].read().image
    assert atlas.width==2048 and atlas.height<=4096,'Atlas exceeds bounded supported dimensions'
    assert atlas.crop((0,0,original.width,original.height)).tobytes()==original.tobytes()
    ms=original_file(prefix+'fontsmeta_'+family)
    me=UnityPy.load(str(ms));ne=UnityPy.load(str(BUNDLES/('fontsmeta_'+family)))
    bm={o.path_id:o for o in me.objects};nm={o.path_id:o for o in ne.objects}
    assert bm.keys()==nm.keys()
    mids=[i for i,o in bm.items() if o.type.name=='TextAsset'];assert len(mids)==1
    mid=mids[0]
    assert all(o.get_raw_data()==nm[i].get_raw_data() for i,o in bm.items() if i!=mid)
    bs=json.loads(bm[mid].read().m_Script)['sprites'];ns=json.loads(nm[mid].read().m_Script)['sprites']
    b={s['id']:s for s in bs};n={s['id']:s for s in ns};assert len(n)==len(ns)
    assert set(b)<=set(n) and not required-{int(i,16) for i in n}
    assert all(n[i]==s for i,s in b.items() if int(i,16) not in cmap or int(i,16) in (32,160))
    generated=[n[f'{cp:x}'] for cp in cmap if cp not in (32,160)]
    for s in generated:
        assert 0<=s['x'] and 0<=s['y'] and s['x']+s['w']<=atlas.width and s['y']+s['h']<=atlas.height
        assert s['trim_x']==1 and s['trim_y']==1
        assert s['trim_w']+2==s['w'] and s['trim_h']+2==s['h']
        g=next(g for g in s['guides'] if g['id']=='base-advance')
        assert all(type(g[k]) is int for k in ('x1','x2','y1','y2'))
    row=next(x for x in build if x['weight']==weight)
    assert row['font_sha256']==sha(f) and row['prototype_sha256']==sha(BUNDLES/('fonts_'+family))
    assert row['metadata_sha256']==sha(BUNDLES/('fontsmeta_'+family))
    assert row['ttf_modified'] is False and row['font_generation_performed'] is False
    checks.append(dict(weight=weight,font_file=f.name,font_sha256=sha(f),required_codepoints=len(required),
        sprites=len(n),bounded_glyph_rectangles=len(generated),atlas_size=atlas.size,
        original_pixels_and_other_objects_preserved=True,unmodified_TTF=True,missing=[]))
# Use the existing compiled layout core to make an independently labeled font
# specimen; no new font outlines, engine execution or screenshot claims.
samples=['هذه أكاديمية كرة المراوغة الشهيرة!','مرحبًا يا أوتو! هل أنت مستعد للمباراة؟',
         'لعبة جديدة — تحميل الحفظ — الإعدادات','صحتك: 100%، مستواك: 12.']
fixtures=[dict(Name=str(i),Text=t,Lines=[dict(Start=0,Limit=len(t))],ProtectNumbers=True) for i,t in enumerate(samples)]
(FONT_DIR/'preview-input.json').write_text(json.dumps(fixtures,ensure_ascii=False,indent=2),'utf8')
subprocess.run([r'C:\Program Files\dotnet\dotnet.exe',str(R/'runtime/LayoutHarness.dll'),
    str(FONT_DIR/'preview-input.json'),str(FONT_DIR/'preview-layout.json')],check=True)
visual=[x['visual'][0] for x in json.loads((FONT_DIR/'preview-layout.json').read_text('utf8'))]
assert '100%' in visual[3] and '12' in visual[3]
im=Image.new('RGB',(1600,1030),'#fffce4');d=ImageDraw.Draw(im)
label=ImageFont.truetype(r'C:\Windows\Fonts\segoeui.ttf',27)
d.text((55,25),'USER-SELECTED FONTS / Standalone specimen, not gameplay',font=label,fill='#713044')
for weight,top,title in [('Regular',90,'LIGHT TEXT — UKIJ Qolyazma Tuz'),('Bold',545,'HEAVY TEXT — Ario Dots 1')]:
    d.rounded_rectangle((35,top,1565,top+430),20,fill='#fffef0',outline='#f74d55',width=4)
    d.text((60,top+18),title,font=label,fill='#713044')
    f=ImageFont.truetype(str(font_file(weight)),52,layout_engine=ImageFont.Layout.BASIC)
    d.text((60,top+65),'Dodgeball Academia! OTTO & Mina: 100%',font=f,fill='#713044')
    for i,t in enumerate(visual):
        width=f.getlength(t);assert width<1460,'Specimen line exceeds available width'
        d.text((1535-width,top+145+i*62),t,font=f,fill='#713044')
im.save(FONT_DIR/'selected-fonts-preview.png')
report=dict(status='supplied original TTFs and serialized Unity font bundles verified on files',
    catalog_sha256=sha(R/'translated-catalog.json'),scope=dict(authored_tables=5189,inline_draft_occurrences=len(inline),credits_draft_roles=len(credits)),
    checks=checks,preview_sha256=sha(FONT_DIR/'selected-fonts-preview.png'),
    font_generation_performed=False,ttf_modification_performed=False,
    standalone_specimen=True,game_screenshot=False,game_launched=False,live_game_written=False,
    user_saves_accessed=False,runtime_verified=False,ready_for_release=False)
(FONT_DIR/'asset-verification-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8')
print(json.dumps(dict(required_codepoints=len(required),fonts_verified=len(checks),inline_drafts=len(inline),credits_drafts=len(credits),runtime_verified=False)))

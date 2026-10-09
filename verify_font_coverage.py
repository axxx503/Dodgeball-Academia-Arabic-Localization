"""Coverage of authored visible text and independently shaped output.
Resource presence alone does not establish successful gameplay rendering.
"""
from pathlib import Path
import hashlib,json,sys,unicodedata
R=Path(__file__).resolve().parent
# Install declared Python dependencies in your environment.
from fontTools.ttLib import TTFont
import UnityPy,arabic_reshaper
from markup import parse,visible_text
from revision_inputs import FRIENDLY,FONT_DIR,BUNDLES,font_file
def sha(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
catalog_sha=sha(R/'translated-catalog.json')
font_inputs=[font_file(weight) for weight in ('Regular','Bold')]
font_inputs += [BUNDLES/f'fontsmeta_{family}' for family in ('bekind','questrian')]
font_hashes={str(p.relative_to(R)).replace('\\','/'):sha(p) for p in font_inputs}
catalog=json.loads((R/'translated-catalog.json').read_text('utf8'))
corpus=[visible_text(parse(r['ar'])) for r in catalog if r['ar']]
shaper=arabic_reshaper.ArabicReshaper(configuration={'delete_harakat':False,'support_ligatures':False})
required={ord(c) for t in corpus for c in t+shaper.reshape(t) if not c.isspace() and c!='|'}
font_missing={};bundle_missing={};counts={}
for weight,family in [('Regular','bekind'),('Bold','questrian')]:
    cmap=TTFont(font_file(weight)).getBestCmap()
    font_missing[weight]=[f'U+{cp:04X} {unicodedata.name(chr(cp),"UNKNOWN")}' for cp in sorted(required-set(cmap))]
    env=UnityPy.load(str(BUNDLES/f'fontsmeta_{family}'))
    meta=json.loads(next(o.read().m_Script for o in env.objects if o.type.name=='TextAsset'))
    ids={int(s['id'],16) for s in meta['sprites']}
    bundle_missing[weight]=[f'U+{cp:04X}' for cp in sorted(required-ids)];counts[weight]=len(ids)
assert sha(R/'translated-catalog.json')==catalog_sha,'Catalog changed during coverage verification'
assert font_hashes=={str(p.relative_to(R)).replace('\\','/'):sha(p) for p in font_inputs},'Font inputs changed during coverage verification'
report=dict(catalog_sha256=catalog_sha,font_inputs_sha256=font_hashes,authored_records=len(corpus),required_codepoints=len(required),missing=font_missing,bundle_missing=bundle_missing,bundle_glyph_counts=counts,unresolved_reference_payloads_not_glyphs=True,runtime_verified=False)
(FONT_DIR/'coverage-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf8')
print(json.dumps(report,ensure_ascii=False))
if any(font_missing.values()) or any(bundle_missing.values()):raise SystemExit(1)

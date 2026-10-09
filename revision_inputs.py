from pathlib import Path
import hashlib,json,os
R=Path(__file__).resolve().parent
FRIENDLY=True;PROVIDED=True;FRIENDLY_TAG='0.6';VERSION='0.6.0-INTERNAL'
FONT_DIR=R/'font-provided';BUNDLES=FONT_DIR/'bundles';PACKAGE=R/'build/candidate-provided-0.6'
def original_file(relative):
    game=os.environ.get('ACADEMIA_ORIGINAL_GAME')
    if not game:raise RuntimeError('Set ACADEMIA_ORIGINAL_GAME to an unmodified original game folder')
    p=Path(game)/relative
    rows={x['path']:x for x in json.loads((R/'SUPPORTED-ORIGINALS.json').read_text('utf8'))}
    expected=rows[relative]['sha256']
    with p.open('rb') as f:actual=hashlib.file_digest(f,'sha256').hexdigest()
    if actual!=expected:raise RuntimeError('Unsupported or modified original input: '+relative)
    return p
def font_file(weight):
    row=next(x for x in json.loads((FONT_DIR/'selected-fonts.json').read_text('utf8'))['fonts'] if x['role']==weight)
    p=FONT_DIR/row['workspace_file']
    with p.open('rb') as f:actual=hashlib.file_digest(f,'sha256').hexdigest()
    if actual!=row['sha256']:raise RuntimeError('Font fingerprint differs')
    return p
def report_file(name):return R/'review'/name

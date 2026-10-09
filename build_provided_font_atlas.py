"""Render USER-SUPPLIED unmodified TTF files into workspace Unity SDF bundles.
No glyph design, new font, TTF modification or synthetic bolding is performed.
Retains original non-overridden glyphs, bundle object IDs and texture names.
Does NOT install or imply the engine has been visually verified.
"""
from pathlib import Path
import sys,json,math,hashlib
R=Path(__file__).resolve().parent
# Install declared Python dependencies in your environment.
from PIL import Image,ImageDraw,ImageFont
from fontTools.ttLib import TTFont
import UnityPy
from revision_inputs import FRIENDLY,original_file
SELECTED=R/"font-provided"
selection=json.loads((SELECTED/"selected-fonts.json").read_text("utf8"))
FONT_FILES={x["role"]:SELECTED/x["workspace_file"] for x in selection["fonts"]}
EXPECTED={x["role"]:x["sha256"] for x in selection["fonts"]}
assert FRIENDLY,"Use original-file hash guard for provided-font atlas build"
import os
G=Path(os.environ['ACADEMIA_ORIGINAL_GAME'])/'DodgeballAcademia_Data/StreamingAssets/AssetBundles'
OUT=SELECTED/"bundles";OUT.mkdir(parents=True,exist_ok=True)
MARGIN=11

def edt_1d(f):
    n=len(f);v=[0]*n;z=[0.]*(n+1);d=[0.]*n;k=0;z[0]=-float('inf');z[1]=float('inf')
    for q in range(1,n):
        s=((f[q]+q*q)-(f[v[k]]+v[k]*v[k]))/(2*(q-v[k]))
        while s<=z[k]:
            k-=1
            s=((f[q]+q*q)-(f[v[k]]+v[k]*v[k]))/(2*(q-v[k]))
        k+=1;v[k]=q;z[k]=s;z[k+1]=float('inf')
    k=0
    for q in range(n):
        while z[k+1]<q:k+=1
        d[q]=(q-v[k])**2+f[v[k]]
    return d

def edt(mask,target):
    w,h=mask.size;px=list(mask.getdata());out=[0.]*(w*h)
    for y in range(h):out[y*w:(y+1)*w]=edt_1d([0 if (px[y*w+x]>=128)==target else 1e10 for x in range(w)])
    for x in range(w):
        col=edt_1d([out[y*w+x] for y in range(h)])
        for y in range(h):out[y*w+x]=col[y]
    return out

def sdf(mask):
    inside=edt(mask,False);outside=edt(mask,True)
    result=Image.new('L',mask.size)
    result.putdata([max(0,min(255,round(128+(math.sqrt(a)-math.sqrt(b))*127/MARGIN))) for a,b in zip(inside,outside)])
    return result

def glyph_image(f,ch):
    l,t,r,b=f.getbbox(ch,anchor='ls')
    w=max(1,r-l);h=max(1,b-t)
    mask=Image.new('L',(w+2*MARGIN,h+2*MARGIN));d=ImageDraw.Draw(mask)
    d.text((MARGIN-l,MARGIN-t),ch,font=f,anchor='ls',fill=255)
    return sdf(mask),MARGIN-l,MARGIN-t,f.getlength(ch)

def build(family,weight):
    src=G/f'fonts_{family}';meta_src=G/f'fontsmeta_{family}'
    if FRIENDLY:
        prefix='DodgeballAcademia_Data/StreamingAssets/AssetBundles/'
        src=original_file(prefix+src.name);meta_src=original_file(prefix+meta_src.name)
    env=UnityPy.load(str(src));tex=next(o for o in env.objects if o.type.name=='Texture2D');data=tex.read()
    original=data.image
    meta_env=UnityPy.load(str(meta_src));mo=next(o for o in meta_env.objects if o.type.name=='TextAsset');md=mo.read()
    meta=json.loads(md.m_Script);sprites={x['id']:x for x in meta['sprites']}
    fontpath=FONT_FILES[weight]
    assert hashlib.sha256(fontpath.read_bytes()).hexdigest()==EXPECTED[weight],"Font input changed"
    f=ImageFont.truetype(str(fontpath),60,layout_engine=ImageFont.Layout.BASIC)
    cmap=TTFont(fontpath).getBestCmap()
    images=[]
    for cp in sorted(cmap):
        if cp in (32,160):continue # preserve the engine's empty glyph/space conventions
        im,bx,by,adv=glyph_image(f,chr(cp));images.append((cp,im,bx,by,adv))
    width=2048;x=1;y=original.height+2;rowh=0;placements=[]
    for cp,im,bx,by,adv in images:
        if x+im.width+2>width:x=1;y+=rowh+2;rowh=0
        placements.append((x,y,cp,im,bx,by,adv));x+=im.width+2;rowh=max(rowh,im.height)
    height=y+rowh+2
    atlas=Image.new('RGBA',(width,height),(255,255,255,0));atlas.paste(original,(0,0))
    for x,y,cp,im,bx,by,adv in placements:
        rgba=Image.new('RGBA',im.size,'white');rgba.putalpha(im);atlas.paste(rgba,(x,y))
        # Native GlyphData.LoadJson reads guide coordinates through IntValue.
        # Write actual JSON integers, not float values such as "42.0".
        sprites[f'{cp:x}']={'id':f'{cp:x}','src':data.m_Name+'.png','x':x-1,'y':y-1,'w':im.width+2,'h':im.height+2,'trim_x':1,'trim_y':1,'trim_w':im.width,'trim_h':im.height,'guides':[{'kind':'vector','id':'base-advance','x1':round(bx+1),'y1':round(by+1),'x2':round(bx+adv+1),'y2':round(by+1)}]}
    md.m_Script=json.dumps({'sprites':list(sprites.values())},ensure_ascii=False,separators=(',',':'));md.save()
    data.image=atlas
    data.save()
    dest=OUT/src.name;dest.write_bytes(env.file.save())
    meta_dest=OUT/meta_src.name;meta_dest.write_bytes(meta_env.file.save())
    # Re-open the serialized prototype and verify new objects/bytes, not just the source image.
    reopened=UnityPy.load(str(dest));actual=next(o.read().image for o in reopened.objects if o.type.name=='Texture2D')
    assert actual.size==atlas.size and actual.tobytes()==atlas.tobytes()
    reopened_meta=UnityPy.load(str(meta_dest));m=json.loads(next(o.read().m_Script for o in reopened_meta.objects if o.type.name=='TextAsset'))
    assert len(m['sprites'])==len(sprites)
    ids={s['id'] for s in m['sprites']}
    assert all(f'{cp:x}' in ids for cp in cmap)
    assert all(type(g[k]) is int for s in m['sprites'] for g in s['guides'] if g['id']=='base-advance' for k in ('x1','x2','y1','y2'))
    atlas.getchannel('A').save(OUT/f'{family}-sdf.png')
    assert hashlib.sha256(fontpath.read_bytes()).hexdigest()==EXPECTED[weight],"Original provided TTF changed"
    print(f"Completed {family}/{weight}",flush=True)
    return dict(font_file=fontpath.name,font_sha256=EXPECTED[weight],ttf_modified=False,font_generation_performed=False,family=family,weight=weight,glyphs=len(sprites),generated_glyphs=len(images),atlas_size=atlas.size,roundtrip_texture_and_metadata=True,original_sha256=hashlib.sha256(src.read_bytes()).hexdigest(),prototype_sha256=hashlib.sha256(dest.read_bytes()).hexdigest(),metadata_sha256=hashlib.sha256(meta_dest.read_bytes()).hexdigest(),runtime_verified=False)

if __name__=='__main__':
    # Small exact-distance checks protect the SDF sign and margin before atlas work.
    assert edt_1d([0,1e10,1e10,1e10])==[0,1,4,9]
    m=Image.new('L',(31,31));ImageDraw.Draw(m).rectangle((10,10,20,20),fill=255)
    s=sdf(m);assert s.getpixel((15,15))>128 and s.getpixel((0,0))<128
    report=[build('bekind','Regular'),build('questrian','Bold')]
    (OUT/'atlas-build-report.json').write_text(json.dumps(report,indent=2),'utf8')
    print(json.dumps(report))

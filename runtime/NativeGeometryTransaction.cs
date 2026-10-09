// Uses generated property accessors, never guessed native pointer offsets.
// Measurements follow the inspected 2019.4.21f1 BuildRecursive implementation.
using System;
using System.Collections.Generic;
using System.Linq;
namespace AcademiaArabic
{
    public sealed class NativeGlyphSnapshot
    {
        public TextRenderer.GlyphToDraw Target;
        public GlyphData.Glyph Glyph;
        public char Character;
        public float X,Y,W,H,Left,Right;
        public NativeGlyphSnapshot(TextRenderer.GlyphToDraw g)
        {Target=g;Glyph=g.glyph;Character=g.c;X=g.x;Y=g.y;W=g.w;H=g.h;Left=g.left;Right=g.right;}
        public void Write()
        {Target.glyph=Glyph;Target.c=Character;Target.x=X;Target.y=Y;Target.w=W;Target.h=H;Target.left=Left;Target.right=Right;}
    }
    public sealed class NativeGeometryTransaction
    {
        public readonly List<NativeGlyphSnapshot> Before=new List<NativeGlyphSnapshot>();
        public readonly List<NativeGlyphSnapshot> After=new List<NativeGlyphSnapshot>();
        public GeometryResult Plan;
        static float Height(FontFamily f)
        {
            if(f==null||f.glyphData==null||f.glyphData.Count==0)throw new InvalidOperationException("Missing font group");
            var first=f.glyphData[0];float h=first.baseToTopSize+first.baseToBottomSize;
            if(!float.IsFinite(h)||h<=0)throw new InvalidOperationException("Invalid font group height");return h;
        }
        static float Scale(FontFamily f,float size,GlyphData.Glyph g)=>size/Height(f)*g.sizeMult;
        static void Close(float a,float b,string message)
        {if(!float.IsFinite(a)||!float.IsFinite(b)||Math.Abs(a-b)>Math.Max(0.002f,Math.Abs(a)*0.0001f))throw new InvalidOperationException(message);}
        public static NativeGeometryTransaction Create(NativeProjection p,SectionState state)
        {
            var tx=new NativeGeometryTransaction();var cells=new List<MeasuredCell>();
            var targets=new Dictionary<int,NativeGlyphSnapshot>();
            var alignmentByLine=new Dictionary<int,float>();
            foreach(var c in p.Characters)
            {
                int i=c.SourceIndex;char ch=state.Logical[i];var cell=new MeasuredCell{SourceIndex=i};
                if(c.Glyph!=null)
                {
                    var g=c.Glyph;var before=new NativeGlyphSnapshot(g);tx.Before.Add(before);targets.Add(i,before);
                    cell.Kind=ch=='\ufffc'?MeasuredKind.Sprite:MeasuredKind.Text;
                    cell.Advance=g.right-g.left;cell.InkOffsetX=g.x-g.left;cell.InkWidth=g.w;
                    if(cell.Kind==MeasuredKind.Text)
                    {
                        float scale=Scale(state.Fonts[i],state.Sizes[i],g.glyph);
                        float margin=g.w/scale-g.glyph.width;
                        Close(margin,g.h/scale-g.glyph.height,"Native outline width/height margin differs");
                        // BuildSection aligns ink x after BuildRecursive; pen
                        // left/right remain unaligned. The offset is per line.
                        float alignment=g.x-g.left-(margin-g.glyph.leftX)*scale;
                        if(alignmentByLine.TryGetValue(c.LineIndex,out var expected))
                            Close(alignment,expected,"Native per-line alignment differs");
                        else alignmentByLine.Add(c.LineIndex,alignment);
                        Close(g.fontSize,state.Sizes[i],"Inherited section size differs");
                    }
                }
                else if(ch==' '||ch=='\u00a0')
                {
                    cell.Kind=MeasuredKind.Space;var font=state.Fonts[i];
                    if(font==null||!font.TryGetGlyph(' ',out var space)||space==null)
                        throw new InvalidOperationException("Exact native space glyph is unavailable");
                    // Native space path deliberately does NOT multiply glyph.sizeMult.
                    cell.Advance=(space.rightX-space.leftX)*state.Sizes[i]/Height(font);
                }
                else if(ch=='\n'||ch=='\0'){cell.Kind=MeasuredKind.Control;}
                else throw new NotSupportedException("Unmeasured native control character");
                cells.Add(cell);
            }
            // Native TrimWhitespaceBounds initializes left to zero and keeps
            // positive leading-space padding. Reorder only the occupied glyph
            // span, retaining that native padding and existing line alignment.
            var origins=p.Lines.Select(line=>line.glyphs.Count==0?line.left:
                Enumerable.Range(0,line.glyphs.Count).Min(i=>line.glyphs[i].left)).ToArray();
            var replacements=new Dictionary<int,GlyphData.Glyph>();
            DisplayMetrics Mirror(int i,char display)
            {
                var old=targets[i];var font=state.Fonts[i];
                if(font==null||!font.TryGetGlyph(display,out var next)||next==null)
                    throw new InvalidOperationException("Mirrored glyph is unavailable");
                float oldScale=Scale(font,state.Sizes[i],old.Glyph),scale=Scale(font,state.Sizes[i],next);
                float margin=old.W/oldScale-old.Glyph.width;
                float alignment=old.X-old.Left-(margin-old.Glyph.leftX)*oldScale;
                // Retain native kerning already reflected in the source cell.
                float kern=cells[i].Advance-(old.Glyph.rightX-old.Glyph.leftX)*oldScale;
                replacements[i]=next;
                return new DisplayMetrics{Advance=(next.rightX-next.leftX)*scale+kern,
                    InkOffsetX=alignment+(margin-next.leftX)*scale,InkWidth=(next.width+margin)*scale};
            }
            tx.Plan=MeasuredLineGeometry.Plan(state.Logical,p.Ranges,cells,origins,Mirror);
            for(int l=0;l<p.Lines.Count;l++)
            {
                var line=p.Lines[l];float occupied=line.glyphs.Count==0?0:
                    Enumerable.Range(0,line.glyphs.Count).Max(i=>line.glyphs[i].right)-origins[l];
                Close(tx.Plan.Widths[l],occupied,"Native reflow/bounds differ from measured plan");
            }
            foreach(var item in tx.Plan.Placements)
            {
                var old=targets[item.SourceIndex];var after=new NativeGlyphSnapshot(old.Target);
                after.X=item.InkX;after.Left=item.PenX;after.Right=item.PenX+item.Advance;
                if(!item.Sprite)
                {
                    after.Character=item.DisplayChar;
                    if(replacements.TryGetValue(item.SourceIndex,out var next))
                    {
                        int i=item.SourceIndex;var font=state.Fonts[i];float oldScale=Scale(font,state.Sizes[i],old.Glyph);
                        float margin=old.W/oldScale-old.Glyph.width,scale=Scale(font,state.Sizes[i],next);
                        float baseline=old.Y+(old.Glyph.baseY+margin)*oldScale;
                        after.Glyph=next;after.Y=baseline-(next.baseY+margin)*scale;
                        after.W=(next.width+margin)*scale;after.H=(next.height+margin)*scale;
                    }
                }
                tx.After.Add(after);
            }
            return tx;
        }
        public void Commit()
        {
            // All source mapping, metric and bounds validation completes first.
            try{foreach(var update in After)update.Write();}
            catch{foreach(var saved in Before)saved.Write();throw;}
        }
    }
}

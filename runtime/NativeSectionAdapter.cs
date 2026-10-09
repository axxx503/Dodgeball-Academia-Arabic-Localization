// Engineering adapter: compiled against THIS game's generated interops.
// Not installed or hooked. Geometry planning and in-game tests remain required.
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
namespace AcademiaArabic
{
    public sealed class SectionSpan
    {
        public TextRenderer.SectionText Section;
        public FontFamily Font;
        public string Original;
        public int Start;
        public float Size;
    }
    public sealed class SectionState
    {
        public List<SectionSpan> Spans=new List<SectionSpan>();
        public string Logical;
        public string Shaped;
        public int MapStart;
        public bool Applied;
        public List<FontFamily> Fonts=new List<FontFamily>();
        public List<float> Sizes=new List<float>();
    }
    public sealed class NativeCharacter
    {
        public int SourceIndex;
        public char Character;
        public int GlyphIndex;
        public int LineIndex;
        public TextRenderer.GlyphToDraw Glyph;
    }
    public sealed class NativeProjection
    {
        public string Logical;
        public List<NativeCharacter> Characters=new List<NativeCharacter>();
        public List<TextRenderer.BuildData.Line> Lines=new List<TextRenderer.BuildData.Line>();
        public LineRange[] Ranges;
    }
    public static class NativeSectionAdapter
    {
        private static bool Arabic(char c)=>(c>='\u0600'&&c<='\u06ff')||(c>='\ufb50'&&c<='\ufeff');
        public static SectionState Prepare(TextRenderer renderer,TextRenderer.Section root,FontFamily initialFont,float initialSize=1)
        {
            var state=new SectionState();var text=new StringBuilder();
            var pointers=new HashSet<IntPtr>();
            void Visit(TextRenderer.Section section,FontFamily font,float size)
            {
                if(section==null)return;
                // Reject shared/cyclic section graphs rather than mutate the same
                // object twice with inconsistent offsets.
                if(!pointers.Add(section.Pointer))throw new InvalidOperationException("Repeated section object");
                var fontSection=section.TryCast<TextRenderer.SectionFont>();
                if(fontSection!=null)font=fontSection.font;
                var sizeSection=section.TryCast<TextRenderer.SectionSize>();
                if(sizeSection!=null)size*=sizeSection.size;
                if(!float.IsFinite(size)||size<=0)throw new InvalidOperationException("Invalid inherited font size");
                var leaf=section.TryCast<TextRenderer.SectionText>();
                if(leaf!=null)
                {
                    var original=leaf.text??"";
                    state.Spans.Add(new SectionSpan{Section=leaf,Font=font,Original=original,Start=text.Length,Size=size});
                    text.Append(original);
                    foreach(char c in original){state.Fonts.Add(font);state.Sizes.Add(size);}
                }
                if(section.TryCast<TextRenderer.SectionSprite>()!=null){text.Append('\ufffc');state.Fonts.Add(font);state.Sizes.Add(size);}
                var children=section.innerSections;
                if(children!=null)for(int i=0;i<children.Count;i++)Visit(children[i],font,size);
            }
            Visit(root,initialFont,initialSize);state.Logical=text.ToString();
            if(!state.Logical.Any(Arabic))return null;
            if(state.Logical.Any(c=>char.IsSurrogate(c)||c=='\ufff9'||c=='\ufffa'||c=='\ufffb'))
                throw new NotSupportedException("Ruby/surrogate layout needs an explicit adapter");
            state.Shaped=ArabicLayout.ShapeLogical(state.Logical);
            if(state.Shaped.Length!=state.Logical.Length)throw new InvalidOperationException("Source index length changed");
            foreach(var span in state.Spans)
                for(int i=0;i<span.Original.Length;i++)
                {
                    char shaped=state.Shaped[span.Start+i];
                    if(shaped==span.Original[i])continue;
                    if(span.Font==null||!span.Font.TryGetGlyph(shaped,out var glyph)||glyph==null)
                        throw new InvalidOperationException("Contextual glyph absent: U+"+((int)shaped).ToString("X4"));
                }
            state.MapStart=renderer.characterToGlyphIndexMap.Count;
            // All allocation/font checks finish before the first write.
            state.Applied=true;
            try
            {
                foreach(var span in state.Spans)
                    span.Section.text=state.Shaped.Substring(span.Start,span.Original.Length);
            }
            catch{Restore(state);throw;}
            return state;
        }
        public static void Restore(SectionState state)
        {
            if(state==null||!state.Applied)return;
            foreach(var span in state.Spans)span.Section.text=span.Original;
            state.Applied=false;
        }
        public static NativeProjection Inspect(TextRenderer renderer,TextRenderer.BuildData data,SectionState state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            var projection=new NativeProjection{Logical=state.Logical};
            var glyphs=new List<(TextRenderer.GlyphToDraw Glyph,int Line)>();
            for(int l=0;l<data.lines.Count;l++)
            {
                var line=data.lines[l];projection.Lines.Add(line);
                for(int i=0;i<line.glyphs.Count;i++)
                {
                    var glyph=line.glyphs[i];
                    if(glyph.furigana)throw new NotSupportedException("Unexpected furigana glyph");
                    glyphs.Add((glyph,l));
                }
            }
            if(glyphs.Count!=data.currentGlyphIndex)throw new InvalidOperationException("Native glyph counter differs from line order");
            var map=renderer.characterToGlyphIndexMap;
            if(map.Count-state.MapStart!=state.Shaped.Length)throw new InvalidOperationException("Native character count differs from resolved source");
            var seen=new HashSet<int>();
            for(int i=0;i<state.Shaped.Length;i++)
            {
                var entry=map[state.MapStart+i];var c=state.Shaped[i];
                bool sprite=c=='\ufffc';
                if(entry.c!=(sprite?'\u2222':c))throw new InvalidOperationException("Native source character mismatch at "+i);
                var item=new NativeCharacter{SourceIndex=i,Character=state.Logical[i],GlyphIndex=entry.index,LineIndex=-1};
                if(entry.index>=0)
                {
                    if(entry.index>=glyphs.Count||!seen.Add(entry.index))throw new InvalidOperationException("Native glyph mapping loss/duplication");
                    var native=glyphs[entry.index];item.Glyph=native.Glyph;item.LineIndex=native.Line;
                    if(sprite!=(native.Glyph.originSectionSprite!=null))throw new InvalidOperationException("Sprite/source mismatch");
                    if(!sprite&&native.Glyph.c!=c)throw new InvalidOperationException("Glyph character mismatch");
                }
                else if(c!=' '&&c!='\u00a0'&&c!='\n'&&c!='\0')
                    throw new InvalidOperationException("Unexpected null glyph U+"+((int)c).ToString("X4"));
                projection.Characters.Add(item);
            }
            if(seen.Count!=glyphs.Count)throw new InvalidOperationException("Unmapped glyphs in native lines");
            projection.Ranges=NativeLineRanges.Project(state.Logical,
                projection.Characters.Select(c=>c.LineIndex).ToArray(),projection.Lines.Count);
            // Spaces intentionally stay in Characters even though native Line.glyphs
            // omits them. The geometry planner must account for their measured gaps.
            return projection;
        }
    }
}

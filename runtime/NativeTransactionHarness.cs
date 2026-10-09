// Offline native-field model only; never described as actual game execution.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using AcademiaArabic;
public class FontFamily
{
    public List<GlyphData> glyphData=new List<GlyphData>{new GlyphData()};
    public bool Missing;
    public bool TryGetGlyph(int c,out GlyphData.Glyph g)
    {g=Missing?null:new GlyphData.Glyph{parent=glyphData[0],leftX=0,rightX=Mark((char)c)?0:c==' '?4:10,width=Mark((char)c)?5:20,height=30,baseY=22,sizeMult=c==' '?9:1};return g!=null;}
    static bool Mark(char c)=>CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark;
}
public class GlyphData
{
    public float baseToTopSize=35,baseToBottomSize=15;
    public class Glyph {public GlyphData parent;public float sizeMult,leftX,rightX,width,height,baseY;}
}
public class TextRenderer
{
    public List<GlyphIndex> characterToGlyphIndexMap=new List<GlyphIndex>();
    public class Section
    {
        static long counter;public IntPtr Pointer=(IntPtr)(++counter);
        public List<Section> innerSections=new List<Section>();
        public T TryCast<T>() where T:class=>this as T;
    }
    public class SectionText:Section{public string text;}
    public class SectionFont:Section{public FontFamily font;}
    public class SectionSize:Section{public float size;}
    public class SectionSprite:Section{}
    public struct GlyphIndex{public char c;public int index;}
    public class GlyphToDraw
    {
        public char c;public GlyphData.Glyph glyph;public float fontSize,y,w,h,left,right;
        public bool furigana;public SectionSprite originSectionSprite;
        float value;public bool FailOnce;
        public float x {get=>value;set{if(FailOnce){FailOnce=false;throw new InvalidOperationException("simulated setter failure");}this.value=value;}}
    }
    public class BuildData
    {
        public int currentGlyphIndex;public List<Line> lines=new List<Line>();
        public class Line{public float left,right;public List<GlyphToDraw> glyphs=new List<GlyphToDraw>();}
    }
}
class NativeTransactionHarness
{
    static List<string> cases=new List<string>();
    static void Assert(bool yes,string why){if(!yes)throw new Exception(why);}
    static void Case(string name,Action action){action();cases.Add(name);}
    static void Reject(string name,Action action)
    {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}catch(NotSupportedException){rejected=true;}Assert(rejected,name);cases.Add(name);}
    static (SectionState State,NativeProjection Projection) Make(string logical,float margin=0)
    {
        var font=new FontFamily();var state=new SectionState{Logical=logical,Shaped=ArabicLayout.ShapeLogical(logical)};
        var p=new NativeProjection{Logical=logical};var line=new TextRenderer.BuildData.Line{left=7,right=7};p.Lines.Add(line);
        for(int i=0;i<logical.Length;i++)
        {
            state.Fonts.Add(font);state.Sizes.Add(20);char c=state.Shaped[i];var native=new NativeCharacter{SourceIndex=i,Character=logical[i],LineIndex=-1,GlyphIndex=-1};
            if(c==' '||c=='\u00a0'){line.right+=4*20f/50;}
            else if(c=='\0'){ /* Native invisible dialogue source position. */ }
            else
            {
                font.TryGetGlyph(c,out var g);float scale=20f/50*g.sizeMult;
                var box=new TextRenderer.GlyphToDraw{c=c,glyph=g,fontSize=20,left=line.right,right=line.right+(g.rightX-g.leftX)*scale,
                    x=line.right+(margin-g.leftX)*scale,y=13-(g.baseY+margin)*scale,w=(g.width+margin)*scale,h=(g.height+margin)*scale};
                if(c=='\ufffc')box.originSectionSprite=new TextRenderer.SectionSprite();
                native.Glyph=box;native.LineIndex=0;native.GlyphIndex=line.glyphs.Count;line.glyphs.Add(box);line.right=box.right;
            }
            p.Characters.Add(native);
        }
        // Native TrimWhitespaceBounds discards leading/trailing virtual spaces.
        if(line.glyphs.Count>0){line.left=line.glyphs[0].left;line.right=line.glyphs.Last().right;}
        else {line.left=0;line.right=0;}
        p.Ranges=new[]{new LineRange{Start=0,Limit=logical.Length}};return(state,p);
    }
    static void Main(string[] args)
    {
        Case("RTL actual native-field positions preserve source identity",()=>{
            var m=Make("سلام");var before=m.Projection.Lines[0].glyphs.ToArray();var tx=NativeGeometryTransaction.Create(m.Projection,m.State);tx.Commit();
            Assert(before.SequenceEqual(m.Projection.Lines[0].glyphs),"glyph list reordered");
            Assert(before[0].left>before.Last().left,"RTL positions not reversed");
        });
        Case("native outline metric formula",()=>{var m=Make("مرحبا",2);NativeGeometryTransaction.Create(m.Projection,m.State).Commit();});
        Case("virtual space ignores space glyph size multiplier",()=>{var m=Make("سلام 100%");NativeGeometryTransaction.Create(m.Projection,m.State).Commit();});
        Case("native zero bound retains positive leading-space padding",()=>{var m=Make("  سلام  ");m.Projection.Lines[0].left=0;var start=m.Projection.Lines[0].glyphs.Min(g=>g.left);NativeGeometryTransaction.Create(m.Projection,m.State).Commit();Assert(m.Projection.Lines[0].glyphs.Min(g=>g.left)==start,"native leading padding lost");});
        Case("inline sprite retains native object and source list",()=>{var m=Make("أ \ufffc ب");var g=m.Projection.Characters[2].Glyph;NativeGeometryTransaction.Create(m.Projection,m.State).Commit();Assert(g.originSectionSprite!=null,"sprite lost");});
        Case("mirrored punctuation glyph selection",()=>{var m=Make("سلام (أ)");var tx=NativeGeometryTransaction.Create(m.Projection,m.State);tx.Commit();Assert(m.Projection.Characters[5].Glyph.c==')',"mirror missing");});
        Case("zero advance diacritic attachment",()=>{var m=Make("لُغَة");NativeGeometryTransaction.Create(m.Projection,m.State).Commit();});
        foreach(float offset in new[]{-13f,-26f})Case("aligned RTL and mirrored punctuation retain ink shift "+offset,()=>{
            var baseline=Make("سلام (أ)",2);var shifted=Make("سلام (أ)",2);
            foreach(var g in shifted.Projection.Lines[0].glyphs)g.x+=offset;
            var a=NativeGeometryTransaction.Create(baseline.Projection,baseline.State);
            var b=NativeGeometryTransaction.Create(shifted.Projection,shifted.State);a.Commit();b.Commit();
            for(int i=0;i<a.After.Count;i++){
                Assert(Math.Abs(b.After[i].X-a.After[i].X-offset)<0.002f,"aligned ink shift lost");
                Assert(b.After[i].Left==a.After[i].Left&&b.After[i].Right==a.After[i].Right,"native aligned pens changed");
                Assert(b.After[i].Character==a.After[i].Character,"mirroring differs");
            }
        });
        Case("wrapped lines may have distinct alignment offsets",()=>{
            var first=Make("سلام");var second=Make("مرحبا");
            foreach(var g in first.Projection.Lines[0].glyphs)g.x-=8;
            foreach(var g in second.Projection.Lines[0].glyphs)g.x-=10;
            var p=first.Projection;var s=first.State;int start=s.Logical.Length;
            s.Logical+=second.State.Logical;s.Shaped+=second.State.Shaped;s.Fonts.AddRange(second.State.Fonts);s.Sizes.AddRange(second.State.Sizes);
            foreach(var c in second.Projection.Characters){c.SourceIndex+=start;c.LineIndex=1;p.Characters.Add(c);}
            p.Logical=s.Logical;p.Lines.Add(second.Projection.Lines[0]);p.Ranges=new[]{new LineRange{Start=0,Limit=start},new LineRange{Start=start,Limit=s.Logical.Length}};
            NativeGeometryTransaction.Create(p,s).Commit();
        });
        Reject("inconsistent ink alignment aborts before writes",()=>{var m=Make("سلام");m.Projection.Characters[1].Glyph.x-=4;NativeGeometryTransaction.Create(m.Projection,m.State);});
        Reject("metric mismatch aborts before any write",()=>{var m=Make("سلام");m.Projection.Characters[0].Glyph.w+=2;NativeGeometryTransaction.Create(m.Projection,m.State);});
        Reject("unmeasured interior gap rejects unsupported reflow",()=>{var m=Make("سلام");m.Projection.Characters.Last().Glyph.left+=2;m.Projection.Characters.Last().Glyph.right+=2;NativeGeometryTransaction.Create(m.Projection,m.State);});
        Reject("missing virtual space glyph rejected",()=>{var m=Make("أ ب");m.State.Fonts[1].Missing=true;NativeGeometryTransaction.Create(m.Projection,m.State);});
        Case("partial setter failure rolls back every glyph",()=>{
            var m=Make("سلام");var tx=NativeGeometryTransaction.Create(m.Projection,m.State);var g=m.Projection.Characters[1].Glyph;g.FailOnce=true;
            bool threw=false;try{tx.Commit();}catch(InvalidOperationException){threw=true;}Assert(threw,"expected setter failure");
            foreach(var b in tx.Before)Assert(b.Target.x==b.X&&b.Target.left==b.Left&&b.Target.right==b.Right&&b.Target.c==b.Character&&b.Target.glyph==b.Glyph,"rollback incomplete");
        });
        Case("nested section sizes multiply and logical text restores",()=>{
            var renderer=new TextRenderer();var size=new TextRenderer.SectionSize{size=0.5f};var leaf=new TextRenderer.SectionText{text="سلام"};size.innerSections.Add(leaf);
            var state=NativeSectionAdapter.Prepare(renderer,size,new FontFamily(),20);Assert(state.Sizes.All(s=>s==10),"size inheritance differs");
            Assert(leaf.text!=state.Logical,"shaping not applied");NativeSectionAdapter.Restore(state);Assert(leaf.text=="سلام","logical text not restored");
        });
        Reject("cyclic section graph rejected before text mutation",()=>{var section=new TextRenderer.Section();section.innerSections.Add(section);NativeSectionAdapter.Prepare(new TextRenderer(),section,new FontFamily(),20);});

        foreach(var text in new[]{"\0سلام\0", "س\0لام", "مرحبا\0 يا أوتو!", "مرحبا\0 يا أوتو!\0", "\0لُغَة\0"})
        Case("dialogue NUL markers preserve native map and RTL: "+text.Replace("\0","[NUL]"),()=>{
            var leaf=new TextRenderer.SectionText{text=text};var renderer=new TextRenderer();
            var state=NativeSectionAdapter.Prepare(renderer,leaf,new FontFamily(),20);
            var native=Make(state.Shaped);var data=new TextRenderer.BuildData();data.lines.AddRange(native.Projection.Lines);
            data.currentGlyphIndex=native.Projection.Lines.Sum(l=>l.glyphs.Count);
            foreach(var c in native.Projection.Characters)renderer.characterToGlyphIndexMap.Add(new TextRenderer.GlyphIndex{c=state.Shaped[c.SourceIndex],index=c.GlyphIndex});
            var projection=NativeSectionAdapter.Inspect(renderer,data,state);
            var tx=NativeGeometryTransaction.Create(projection,state);tx.Commit();
            var plain=Make(text.Replace("\0",""));var plainTx=NativeGeometryTransaction.Create(plain.Projection,plain.State);plainTx.Commit();
            Assert(tx.After.Count==plainTx.After.Count,"marker became drawable");
            for(int i=0;i<tx.After.Count;i++){
                Assert(tx.After[i].Character==plainTx.After[i].Character,"marker broke joining or punctuation");
                Assert(Math.Abs(tx.After[i].X-plainTx.After[i].X)<0.002f,"marker changed visual position");
            }
            Assert(projection.Characters.Count==text.Length,"typewriter source indices changed");
            Assert(projection.Characters.Where(c=>text[c.SourceIndex]=='\0').All(c=>c.GlyphIndex==-1),"marker index changed");
            NativeSectionAdapter.Restore(state);Assert(leaf.text==text,"dialogue commands lost on restore");
        });
        Case("dialogue wrapped style sections retain markers and prior map entries",()=>{
            var renderer=new TextRenderer();renderer.characterToGlyphIndexMap.Add(new TextRenderer.GlyphIndex{c='x',index=0});
            var root=new TextRenderer.Section();var firstLeaf=new TextRenderer.SectionText{text="\0سلام "};root.innerSections.Add(firstLeaf);
            var style=new TextRenderer.SectionFont{font=new FontFamily()};var secondLeaf=new TextRenderer.SectionText{text="مرحبا\0"};style.innerSections.Add(secondLeaf);root.innerSections.Add(style);
            var state=NativeSectionAdapter.Prepare(renderer,root,new FontFamily(),20);
            var first=Make(state.Shaped.Substring(0,firstLeaf.text.Length));var second=Make(state.Shaped.Substring(firstLeaf.text.Length));
            var data=new TextRenderer.BuildData();data.lines.Add(first.Projection.Lines[0]);data.lines.Add(second.Projection.Lines[0]);
            int count=0;
            foreach(var m in new[]{first,second}){
                foreach(var c in m.Projection.Characters)renderer.characterToGlyphIndexMap.Add(new TextRenderer.GlyphIndex{c=m.State.Shaped[c.SourceIndex],index=c.GlyphIndex<0?-1:count+c.GlyphIndex});
                count+=m.Projection.Lines[0].glyphs.Count;
            }
            data.currentGlyphIndex=count;var projection=NativeSectionAdapter.Inspect(renderer,data,state);
            NativeGeometryTransaction.Create(projection,state).Commit();
            Assert(projection.Ranges.Length==2,"wrapped line identities lost");
            Assert(data.lines[0].glyphs[0].left>data.lines[0].glyphs.Last().left && data.lines[1].glyphs[0].left>data.lines[1].glyphs.Last().left,"wrapped RTL missing");
            Assert(renderer.characterToGlyphIndexMap[0].c=='x' && state.MapStart==1,"prior native map altered");
            NativeSectionAdapter.Restore(state);Assert(firstLeaf.text=="\0سلام " && secondLeaf.text=="مرحبا\0","nested command/style source lost");
        });
        Reject("unknown missing drawable still rejected",()=>{
            var text="سلام?";var leaf=new TextRenderer.SectionText{text=text};var renderer=new TextRenderer();
            var state=NativeSectionAdapter.Prepare(renderer,leaf,new FontFamily(),20);var m=Make(state.Shaped.Substring(0,4));
            var data=new TextRenderer.BuildData{currentGlyphIndex=4};data.lines.AddRange(m.Projection.Lines);
            for(int i=0;i<5;i++)renderer.characterToGlyphIndexMap.Add(new TextRenderer.GlyphIndex{c=state.Shaped[i],index=i==4?-1:i});
            NativeSectionAdapter.Inspect(renderer,data,state);
        });
        var report=new{fixtures=cases.Count,cases,errors=new string[0],model="offline stub native fields and transactional setters",compiled_and_executed=true,in_game_verified=false};
        File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Native transaction model passed: "+cases.Count+" fixtures. Not an in-game test.");
    }
}

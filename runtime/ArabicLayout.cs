// Original integration core. No game hooks installed by this file.
// Input is resolved, logical text AFTER game commands/substitutions are parsed.
// Shaping is one UTF-16 unit per source unit: no ligature collapse and no reversal.
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AcademiaArabic
{
    public sealed class LineRange
    {
        public int Start {get;set;}
        public int Limit {get;set;}
    }
    public sealed class PlannedGlyph
    {
        public int LogicalIndex {get;set;}
        public char DisplayChar {get;set;}
        public byte Level {get;set;}
    }
    public static class ArabicLayout
    {
        private static readonly Dictionary<char,char> Bases = Forms.Data
            .SelectMany(x=>x.Value.Where(c=>c!=0).Select(c=>new {Form=c,Base=x.Key}))
            .GroupBy(x=>x.Form).ToDictionary(x=>x.Key,x=>x.First().Base);
        private static bool Transparent(char c)
        {
            var category=CharUnicodeInfo.GetUnicodeCategory(c);
            return category==UnicodeCategory.NonSpacingMark || category==UnicodeCategory.EnclosingMark;
        }
        private static char[] Form(char c)
        {
            if(c=='\u0640'||c=='\u200D')return new[]{c,c,c,c};
            return Forms.Data.TryGetValue(c,out var f)?f:null;
        }
        public static string ShapeLogical(string text)
        {
            var baseChars=text.Select(c=>Bases.TryGetValue(c,out var b)?b:c).ToArray();
            var result=(char[])baseChars.Clone();
            for(int i=0;i<baseChars.Length;i++)
            {
                var f=Form(baseChars[i]);if(f==null || baseChars[i]=='\u200D'||baseChars[i]=='\u0640')continue;
                int p=i-1,n=i+1;
                while(p>=0&&(Transparent(baseChars[p])||baseChars[p]=='\0'))p--;
                while(n<baseChars.Length&&(Transparent(baseChars[n])||baseChars[n]=='\0'))n++;
                var prev=p>=0?Form(baseChars[p]):null;var next=n<baseChars.Length?Form(baseChars[n]):null;
                bool before=prev!=null && (prev[2]!=0||prev[3]!=0) && (f[1]!=0||f[3]!=0);
                bool after=next!=null && (f[2]!=0||f[3]!=0) && (next[1]!=0||next[3]!=0);
                int form=before?(after?3:1):(after?2:0);
                result[i]=f[form]!=0?f[form]:baseChars[i];
            }
            return new string(result);
        }

        // Windows' ICU resolves Unicode BiDi, isolates and paired brackets.
        // The paragraph stays pinned until its child line objects are disposed.
        private const string Icu="icu.dll";
        [DllImport(Icu,CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr ubidi_openSized(int length,int runs,ref int error);
        [DllImport(Icu,CallingConvention=CallingConvention.Cdecl)] private static extern void ubidi_close(IntPtr bidi);
        [DllImport(Icu,CallingConvention=CallingConvention.Cdecl)] private static extern void ubidi_setPara(IntPtr bidi,IntPtr text,int length,byte level,IntPtr embeddingLevels,ref int error);
        [DllImport(Icu,CallingConvention=CallingConvention.Cdecl)] private static extern void ubidi_setLine(IntPtr para,int start,int limit,IntPtr line,ref int error);
        [DllImport(Icu,CallingConvention=CallingConvention.Cdecl)] private static extern void ubidi_getVisualMap(IntPtr bidi,[Out] int[] map,ref int error);
        [DllImport(Icu,CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr ubidi_getLevels(IntPtr bidi,ref int error);
        [DllImport(Icu,CallingConvention=CallingConvention.Cdecl)] private static extern int u_charMirror(int c);
        [DllImport(Icu,CallingConvention=CallingConvention.Cdecl)] private static extern int u_charDirection(int c);
        private static void Check(int e){if(e>0)throw new InvalidOperationException("ICU BiDi error "+e);}
        // Keep an ASCII identifier and its adjacent number/punctuation in ONE
        // isolate. Isolating only 100% in A100% can reorder it as 100%A in RTL.
        // Thousands/decimal separators also belong to the same numeric run.
        private const string LatinToken=@"[A-Za-z_][A-Za-z_0-9]*(?:[.:%/+\-][A-Za-z_0-9]+)*(?:[%+])?";
        // Adjacent English words form one run: separate isolates for Dodgeball
        // and Academia would reverse their order within an Arabic paragraph.
        private static readonly Regex Numeric=new Regex(LatinToken+@"(?:[ \u00a0]+"+LatinToken+@")*|[+\-]?[\p{Nd}]+(?:[.,:\u066B\u066C/\-][\p{Nd}]+)*(?:[%\u066A])?",RegexOptions.CultureInvariant);

        public static List<PlannedGlyph[]> PlanLines(string logical,IReadOnlyList<LineRange> lines,bool protectNumbers=true)
        {
            if(logical.Any(char.IsSurrogate))throw new NotSupportedException("Game glyph planner supports BMP characters only; surrogate glyphs require explicit adapter support.");
            var text=new StringBuilder();var originalIndex=new List<int>();var boundary=new int[logical.Length+1];
            var numeric=protectNumbers?Numeric.Matches(logical).Cast<Match>().ToDictionary(m=>m.Index,m=>m.Length):new Dictionary<int,int>();
            int end=-1;
            for(int i=0;i<logical.Length;i++)
            {
                if(i==end){text.Append('\u2069');originalIndex.Add(-1);end=-1;}
                boundary[i]=text.Length;
                // Isolating the first Latin token must not hide the paragraph's
                // original first strong direction. Otherwise all-English text
                // made of isolates falls back to RTL and reverses word order.
                // A virtual LRM supplies the original LTR base per paragraph;
                // it is removed from output and never changes source indices.
                if(i==0 || logical[i-1]=='\n' || logical[i-1]=='\r' || logical[i-1]=='\u2029')
                    for(int k=i;k<logical.Length && logical[k]!='\n' && logical[k]!='\r' && logical[k]!='\u2029';k++)
                    {
                        int direction=u_charDirection(logical[k]);
                        if(direction==0){text.Append('\u200e');originalIndex.Add(-1);break;}
                        if(direction==1 || direction==13)break;
                    }
                if(numeric.TryGetValue(i,out var length)){text.Append('\u2066');originalIndex.Add(-1);end=i+length;}
                text.Append(logical[i]);originalIndex.Add(i);
            }
            if(end==logical.Length){text.Append('\u2069');originalIndex.Add(-1);}
            boundary[logical.Length]=text.Length;
            var chars=text.ToString().ToCharArray();var pinned=GCHandle.Alloc(chars,GCHandleType.Pinned);
            IntPtr para=IntPtr.Zero;int error=0;var result=new List<PlannedGlyph[]>();
            try
            {
                para=ubidi_openSized(chars.Length,0,ref error);Check(error);
                if(para==IntPtr.Zero)throw new InvalidOperationException("ICU returned null paragraph");
                // Automatic base direction with RTL fallback for neutral Arabic UI.
                ubidi_setPara(para,pinned.AddrOfPinnedObject(),chars.Length,0xff,IntPtr.Zero,ref error);Check(error);
                foreach(var range in lines)
                {
                    if(range.Start<0||range.Limit>logical.Length||range.Start>range.Limit)throw new ArgumentOutOfRangeException("line range");
                    if(range.Start==range.Limit){result.Add(Array.Empty<PlannedGlyph>());continue;}
                    int a=boundary[range.Start],b=boundary[range.Limit];IntPtr line=IntPtr.Zero;
                    try
                    {
                        line=ubidi_openSized(b-a,0,ref error);Check(error);
                        if(line==IntPtr.Zero)throw new InvalidOperationException("ICU returned null line");
                        ubidi_setLine(para,a,b,line,ref error);Check(error);
                        var map=new int[b-a];ubidi_getVisualMap(line,map,ref error);Check(error);
                        var levels=new byte[b-a];Marshal.Copy(ubidi_getLevels(line,ref error),levels,0,levels.Length);Check(error);
                        var plan=new List<PlannedGlyph>();
                        foreach(var pos in map)
                        {
                            if(pos<0||pos>=b-a)throw new InvalidOperationException("Invalid ICU map");
                            int k=originalIndex[a+pos];if(k<0)continue;char c=logical[k];
                            // Mirror punctuation only in the displayed glyph selection;
                            // never mutate source commands or typewriter indices.
                            if((levels[pos]&1)!=0)c=(char)u_charMirror(c);
                            plan.Add(new PlannedGlyph{LogicalIndex=k,DisplayChar=c,Level=levels[pos]});
                        }
                        // ICU's character map reverses NSMs in RTL runs. Keep each
                        // combining cluster base first for zero-advance placement.
                        for(int v=0;v<plan.Count;v++)
                        {
                            if(!Transparent(logical[plan[v].LogicalIndex]))continue;
                            int last=v;while(last<plan.Count&&Transparent(logical[plan[last].LogicalIndex]))last++;
                            if(last<plan.Count && plan[last].LogicalIndex<plan[v].LogicalIndex)
                            {
                                var cluster=plan.GetRange(v,last-v+1).OrderBy(g=>g.LogicalIndex).ToArray();
                                for(int n=0;n<cluster.Length;n++)plan[v+n]=cluster[n];v=last;
                            }
                        }
                        if(!plan.Select(g=>g.LogicalIndex).OrderBy(i=>i).SequenceEqual(Enumerable.Range(range.Start,range.Limit-range.Start)))throw new InvalidOperationException("Glyph index loss/duplication");
                        result.Add(plan.ToArray());
                    }
                    finally{if(line!=IntPtr.Zero)ubidi_close(line);}
                }
            }
            finally{if(para!=IntPtr.Zero)ubidi_close(para);pinned.Free();}
            return result;
        }
    }
}

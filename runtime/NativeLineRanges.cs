// Pure projection of native glyph-to-line identities back to source ranges.
// Whitespace at an automatic wrap remains on the preceding line and is trimmed
// by geometry. Explicit LF creates its own line even when no glyph is drawn.
using System;
using System.Linq;
using System.Collections.Generic;
namespace AcademiaArabic
{
    public static class NativeLineRanges
    {
        public static LineRange[] Project(string text,IReadOnlyList<int> sourceLines,int lineCount)
        {
            if(sourceLines.Count!=text.Length || lineCount<1)
                throw new InvalidOperationException("Missing source line identities");
            var starts=new List<int>{0};int current=0;
            for(int i=0;i<text.Length;i++)
            {
                int line=sourceLines[i];
                if(line< -1 || line>=lineCount)throw new InvalidOperationException("Native line index out of range");
                if(line>=0)
                {
                    if(text[i]=='\n' || text[i]==' ' || text[i]=='\u00a0')
                        throw new InvalidOperationException("Native virtual whitespace unexpectedly drawn");
                    if(line<current || line>current+1)
                        throw new InvalidOperationException("Nonmonotone or ambiguous native line identities");
                    if(line==current+1){starts.Add(i);current=line;}
                }
                else if(text[i]=='\n')
                {
                    current++;starts.Add(i+1);
                    if(current>=lineCount)throw new InvalidOperationException("Newline exceeds native line count");
                }
                else if(text[i]!=' ' && text[i]!='\u00a0' && text[i]!='\u200c' && text[i]!='\u200d' && text[i]!='\0')
                    throw new NotSupportedException("Unaccounted native null glyph");
            }
            if(starts.Count!=lineCount)throw new InvalidOperationException("Unaccounted native empty/wrapped line");
            var ranges=starts.Select((start,l)=>new LineRange{Start=start,Limit=l+1<starts.Count?starts[l+1]:text.Length}).ToArray();
            // Verify every real glyph retains its native line identity.
            for(int l=0;l<ranges.Length;l++)for(int i=ranges[l].Start;i<ranges[l].Limit;i++)
                if(sourceLines[i]>=0 && sourceLines[i]!=l)
                    throw new InvalidOperationException("Projected range moved a glyph between lines");
            return ranges;
        }
    }
}

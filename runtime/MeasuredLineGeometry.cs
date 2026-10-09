// Pure measured-geometry stage. No Unity objects, hooks or native writes.
// The adapter must supply an exact cell for EVERY resolved source character,
// including spaces omitted by the game's glyph list and sprite placeholders.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AcademiaArabic
{
    public enum MeasuredKind { Text, Sprite, Space, Control }
    public sealed class MeasuredCell
    {
        public int SourceIndex;
        public MeasuredKind Kind;
        public float Advance;
        public float InkOffsetX;
        public float InkWidth;
    }
    public sealed class DisplayMetrics
    {
        public float Advance;
        public float InkOffsetX;
        public float InkWidth;
    }
    public sealed class GlyphPlacement
    {
        public int SourceIndex;
        public int LineIndex;
        public char DisplayChar;
        public bool Sprite;
        public float PenX;
        public float InkX;
        public float InkWidth;
        public float Advance;
    }
    public sealed class GeometryResult
    {
        // Kept in original source order. Do not replace the game's logical glyph
        // list with VisualIndices: typewriter timing depends on source indices.
        public GlyphPlacement[] Placements;
        public int[][] VisualIndices;
        public float[] Widths;
    }
    public static class MeasuredLineGeometry
    {
        private static bool Mark(char c)
        {
            var cat=CharUnicodeInfo.GetUnicodeCategory(c);
            return cat==UnicodeCategory.NonSpacingMark || cat==UnicodeCategory.EnclosingMark;
        }
        private static void Finite(float f)
        {
            if(!float.IsFinite(f))throw new InvalidOperationException("Non-finite measured geometry");
        }
        public static GeometryResult Plan(string logical, IReadOnlyList<LineRange> lines,
            IReadOnlyList<MeasuredCell> cells, IReadOnlyList<float> lineOrigins,
            Func<int,char,DisplayMetrics> mirroredMetrics=null)
        {
            if(cells.Count!=logical.Length || lineOrigins.Count!=lines.Count)
                throw new InvalidOperationException("Missing measured source cells or line origins");
            int limit=0;
            foreach(var range in lines)
            {
                if(range.Start!=limit || range.Limit<range.Start || range.Limit>logical.Length)
                    throw new InvalidOperationException("Lines must cover the resolved source contiguously");
                limit=range.Limit;
            }
            if(limit!=logical.Length)throw new InvalidOperationException("Source characters absent from lines");
            for(int i=0;i<cells.Count;i++)
            {
                var m=cells[i]; if(m.SourceIndex!=i)throw new InvalidOperationException("Measured cells out of source order");
                Finite(m.Advance);Finite(m.InkOffsetX);Finite(m.InkWidth);
                if(m.Advance<0 || m.InkWidth<0)throw new InvalidOperationException("Negative advance/width");
                if(Mark(logical[i]) && (m.Kind!=MeasuredKind.Text || m.Advance!=0))
                    throw new InvalidOperationException("Combining marks require explicit zero-advance glyphs");
                bool whitespace=logical[i]==' ' || logical[i]=='\u00a0';
                bool control=logical[i]=='\0' || logical[i]=='\r' || logical[i]=='\n' || logical[i]=='\u200c' || logical[i]=='\u200d';
                if(m.Kind==MeasuredKind.Sprite && logical[i]!='\ufffc')throw new InvalidOperationException("Sprite/source mismatch");
                if(logical[i]=='\ufffc' && m.Kind!=MeasuredKind.Sprite)throw new InvalidOperationException("Unmeasured sprite");
                if(m.Kind==MeasuredKind.Space && !whitespace)throw new InvalidOperationException("Space/source mismatch");
                if(whitespace && m.Kind!=MeasuredKind.Space)throw new InvalidOperationException("Unmeasured virtual space");
                if(m.Kind==MeasuredKind.Control && (!control || m.Advance!=0))throw new InvalidOperationException("Control/source mismatch");
                if(control && m.Kind!=MeasuredKind.Control)throw new InvalidOperationException("Unmeasured control");
            }
            var shaped=ArabicLayout.ShapeLogical(logical);
            var plans=ArabicLayout.PlanLines(shaped,lines);
            var output=new List<GlyphPlacement>(); var widths=new float[lines.Count];
            var visual=new int[lines.Count][];
            var covered=new HashSet<int>();
            for(int l=0;l<lines.Count;l++)
            {
                Finite(lineOrigins[l]);
                var plan=plans[l];visual[l]=plan.Select(g=>g.LogicalIndex).ToArray();
                // Native TrimWhitespaceBounds ignores leading/trailing virtual
                // whitespace. Retain its source mapping, but not its width.
                int first=0,last=plan.Length-1;
                bool Ignored(int pos)=>cells[plan[pos].LogicalIndex].Kind==MeasuredKind.Space ||
                    cells[plan[pos].LogicalIndex].Kind==MeasuredKind.Control;
                while(first<=last && Ignored(first))first++;
                while(last>=first && Ignored(last))last--;
                float pen=lineOrigins[l];GlyphPlacement clusterBase=null;
                for(int v=0;v<plan.Length;v++)
                {
                    var g=plan[v];int i=g.LogicalIndex;var m=cells[i];
                    if(!covered.Add(i))throw new InvalidOperationException("Source index placed twice");
                    if(m.Kind==MeasuredKind.Control)continue;
                    if(m.Kind==MeasuredKind.Space)
                    {
                        if(v>=first && v<=last)pen+=m.Advance;
                        clusterBase=null;continue;
                    }
                    var metrics=new DisplayMetrics{Advance=m.Advance,InkOffsetX=m.InkOffsetX,InkWidth=m.InkWidth};
                    if(g.DisplayChar!=shaped[i])
                    {
                        if(mirroredMetrics==null)throw new InvalidOperationException("Mirrored glyph needs measured metrics");
                        metrics=mirroredMetrics(i,g.DisplayChar) ?? throw new InvalidOperationException("Missing mirrored glyph");
                        Finite(metrics.Advance);Finite(metrics.InkOffsetX);Finite(metrics.InkWidth);
                        if(metrics.InkWidth<0 || Math.Abs(metrics.Advance-m.Advance)>0.001f)
                            throw new InvalidOperationException("Mirrored advance changed: native reflow required");
                    }
                    var placement=new GlyphPlacement{SourceIndex=i,LineIndex=l,DisplayChar=g.DisplayChar,
                        Sprite=m.Kind==MeasuredKind.Sprite,PenX=pen,InkX=pen+metrics.InkOffsetX,
                        InkWidth=metrics.InkWidth,Advance=m.Advance};
                    if(Mark(logical[i]))
                    {
                        // Horizontal centering only. Native adapter retains the
                        // font's mark Y; vertical anchors/stacking need review.
                        if(clusterBase==null || i<=clusterBase.SourceIndex || clusterBase.Sprite)
                            throw new NotSupportedException("Combining mark has no adjacent text base");
                        for(int k=clusterBase.SourceIndex+1;k<i;k++)
                            if(!Mark(logical[k]))throw new NotSupportedException("Detached combining cluster");
                        placement.PenX=clusterBase.PenX;
                        placement.InkX=clusterBase.PenX+clusterBase.Advance/2-metrics.InkWidth/2;
                    }
                    else {clusterBase=placement;pen+=m.Advance;}
                    output.Add(placement);
                }
                widths[l]=pen-lineOrigins[l];Finite(widths[l]);
            }
            if(covered.Count!=logical.Length)throw new InvalidOperationException("Source index missing from geometry");
            return new GeometryResult{Placements=output.OrderBy(p=>p.SourceIndex).ToArray(),
                VisualIndices=visual,Widths=widths};
        }
    }
}

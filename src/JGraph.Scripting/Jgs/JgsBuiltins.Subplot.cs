using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
namespace JGraph.Scripting.Jgs;
internal static partial class JgsBuiltins
{
    private static JgsValue MatlabSubplot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 1 && JgsHandleRegistry.TryGet(args[0],out var selected) && selected.Target is AxesModel selectedAxes)
        { JG.MakeCurrent(selectedAxes); return args[0]; }
        if (args.Count == 1 && args[0].Type == JgsType.Number)
        {
            int digits = Count("subplot",args,0,line,col);
            args = [JgsValue.Number(digits/100),JgsValue.Number(digits/10%10),JgsValue.Number(digits%10)];
        }
        bool custom = args.Count >= 2 && args[0].Type == JgsType.String && args[0].AsString.Equals("Position",StringComparison.OrdinalIgnoreCase);
        double[] position;
        int start;
        if (custom) { position = ToDoubles("subplot Position",args[1],line,col); start=2; }
        else
        {
            if (args.Count < 3) throw new JgsRuntimeException(line,col,"subplot expects rows, columns and positions.");
            int rows=Count("subplot",args,0,line,col), cols=Count("subplot",args,1,line,col);
            double[] slots=ToDoubles("subplot positions",args[2],line,col);
            if (rows<1 || cols<1 || slots.Length==0 || slots.Any(x=>x<1 || x>rows*cols || x!=System.Math.Truncate(x)))
                throw new JgsRuntimeException(line,col,"subplot positions must be whole indices within the grid.");
            int left=slots.Min(x=>((int)x-1)%cols), right=slots.Max(x=>((int)x-1)%cols);
            int top=slots.Min(x=>((int)x-1)/cols), bottom=slots.Max(x=>((int)x-1)/cols);
            double dx=.775/(cols-.24), dy=.815/(rows-.28);
            position=[.13+left*dx,.11+(rows-1-bottom)*dy,(right-left+1-.24)*dx,(bottom-top+1-.28)*dy];
            start=3;
        }
        if (position.Length!=4 || position.Any(x=>!double.IsFinite(x)) || position[2]<=0 || position[3]<=0)
            throw new JgsRuntimeException(line,col,"subplot Position must contain [left bottom width height] with positive size.");
        var bounds = new Rect2D(position[0],1-position[1]-position[3],position[2],position[3]);
        AxesModel? supplied=null; bool replace=false;
        if (start<args.Count && JgsHandleRegistry.TryGet(args[start],out var entry) && entry.Target is AxesModel named) { supplied=named; start++; }
        else if (start<args.Count && args[start].Type==JgsType.String && args[start].AsString.ToLowerInvariant() is "replace" or "align")
        { replace=args[start].AsString.Equals("replace",StringComparison.OrdinalIgnoreCase); start++; }
        int previousFigure = JG.CurrentFigureNumber;
        AxesModel? previousAxes = JG.CurrentAxesOrNull;
        FigureModel figure=supplied?.Parent as FigureModel ?? JG.CurrentFigure;
        var options=args.Skip(start).ToList();
        for (int i=0;i+1<options.Count;i+=2)
            if (options[i].Type==JgsType.String && options[i].AsString.Equals("Parent",StringComparison.OrdinalIgnoreCase))
            {
                figure=JgsHandleRegistry.Require(options[i+1],line,col).Target as FigureModel
                    ?? throw new JgsRuntimeException(line,col,"subplot Parent must be a figure.");
                options.RemoveRange(i,2); break;
            }
        AxesModel? axes=supplied;
        foreach (var existing in figure.Axes.ToArray())
        {
            if (existing==supplied) continue;
            var b=existing.InnerTarget ?? existing.NormalizedBounds;
            bool same=System.Math.Abs(b.X-bounds.X)+System.Math.Abs(b.Y-bounds.Y)+System.Math.Abs(b.Width-bounds.Width)+System.Math.Abs(b.Height-bounds.Height)<1e-9;
            if (same && !replace && supplied is null) { axes=existing; continue; }
            if (b.Left<bounds.Right-1e-10 && b.Right>bounds.Left+1e-10 && b.Top<bounds.Bottom-1e-10 && b.Bottom>bounds.Top+1e-10)
                figure.Axes.Remove(existing);
        }
        axes ??= figure.AddAxes();
        if (axes.Parent != figure)
            using (GraphObjectLifecycle.SuppressNotifications()) { (axes.Parent as FigureModel)?.Axes.Remove(axes); figure.Axes.Add(axes); }
        axes.InnerTarget=bounds; axes.PositionConstraint=PositionConstraintType.InnerPosition;
        JG.MakeCurrent(axes);
        ApplyMenuOptions("subplot",axes,options,0,line,col);
        if (supplied is not null && JG.GetFigureNumber(figure) != previousFigure)
        {
            JG.Figure(previousFigure);
            if (previousAxes is not null && previousAxes.Parent is FigureModel) JG.MakeCurrent(previousAxes);
        }
        JgsHandleRegistry.DropUnreachable();
        return JgsHandleRegistry.For(axes);
    }
}

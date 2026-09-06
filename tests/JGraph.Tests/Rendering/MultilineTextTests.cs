using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Rendering.Skia;
using SkiaSharp;
using Xunit;
namespace JGraph.Tests.Rendering;
public sealed class MultilineTextTests
{
    [Theory]
    [InlineData(TextInterpreter.None)]
    [InlineData(TextInterpreter.Tex)]
    public void MultilineLabelsReserveAndDrawSeparateRows(TextInterpreter interpreter)
    {
        using var surface=SKSurface.Create(new SKImageInfo(160,100));
        surface.Canvas.Clear(SKColors.White);
        using var context=new SkiaRenderContext(surface.Canvas,new Size2D(160,100));
        var style=new TextStyle(Colors.Black,18).WithInterpreter(interpreter);
        var single=context.MeasureText("MMMM",style);
        var multiple=context.MeasureText("MMMM\nMMMM",style);
        Assert.Equal(single.Width,multiple.Width,6);
        Assert.True(multiple.Height>single.Height*2);
        context.DrawText("MMMM\nMMMM",new Point2D(20,10),style,vertical:VerticalAlignment.Top);
        using var snapshot=surface.Snapshot();
        using var bitmap=SKBitmap.FromImage(snapshot);
        int bands=0; bool previous=false;
        for(int y=0;y<bitmap.Height;y++)
        {
            bool ink=Enumerable.Range(0,bitmap.Width).Any(x=>bitmap.GetPixel(x,y).Red<128);
            if(ink&&!previous) bands++;
            previous=ink;
        }
        Assert.Equal(2,bands);
    }
}

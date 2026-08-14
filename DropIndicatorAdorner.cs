using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LaptopQaUsbBuilder;

public sealed class DropIndicatorAdorner : Adorner
{
    private static readonly Brush IndicatorBrush = new SolidColorBrush(Color.FromRgb(32, 184, 106));
    private static readonly Brush IndicatorFill = new SolidColorBrush(Color.FromArgb(40, 32, 184, 106));
    public bool IsAfter { get; }

    private DropIndicatorAdorner(UIElement adornedElement, bool isAfter) : base(adornedElement)
    {
        IsAfter = isAfter;
        IsHitTestVisible = false;
    }

    public static DropIndicatorAdorner? Attach(UIElement element, bool isAfter)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null) return null;
        var indicator = new DropIndicatorAdorner(element, isAfter);
        layer.Add(indicator);
        return indicator;
    }

    public void Detach() => AdornerLayer.GetAdornerLayer(AdornedElement)?.Remove(this);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var pen = new Pen(IndicatorBrush, 2);
        pen.Freeze();
        var bounds = new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2));
        drawingContext.DrawRoundedRectangle(IndicatorFill, pen, bounds, 9, 9);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Zettelkasten.Core.Graph;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.Views;

/// <summary>Draws the open note's link neighbourhood. Brushes are resource references, so theme switches apply live.</summary>
public sealed class LinkGraphView : Canvas
{
    private const int LabelMaxLength = 22;
    private const double Pad = 10;

    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(
        nameof(Graph), typeof(LinkGraph), typeof(LinkGraphView),
        new PropertyMetadata(null, (d, _) => ((LinkGraphView)d).OnGraphChanged()));

    public static readonly DependencyProperty NodeCommandProperty = DependencyProperty.Register(
        nameof(NodeCommand), typeof(ICommand), typeof(LinkGraphView));

    private IReadOnlyDictionary<string, LayoutPoint> _layout = new Dictionary<string, LayoutPoint>();

    public LinkGraphView()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        SizeChanged += (_, _) => Redraw();
    }

    public LinkGraph? Graph
    {
        get => (LinkGraph?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    /// <summary>Executed with the clicked <see cref="GraphNode"/>.</summary>
    public ICommand? NodeCommand
    {
        get => (ICommand?)GetValue(NodeCommandProperty);
        set => SetValue(NodeCommandProperty, value);
    }

    private void OnGraphChanged()
    {
        _layout = Graph is null ? new Dictionary<string, LayoutPoint>() : ForceLayout.Compute(Graph);
        Redraw();
    }

    private Point ToScreen(LayoutPoint p) =>
        new(Pad + p.X * Math.Max(0, ActualWidth - 2 * Pad), Pad + p.Y * Math.Max(0, ActualHeight - 2 * Pad));

    private void Redraw()
    {
        Children.Clear();
        if (Graph is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        foreach (var edge in Graph.Edges)
        {
            if (!_layout.TryGetValue(edge.SourceId, out var a) || !_layout.TryGetValue(edge.TargetId, out var b)) continue;
            var (pa, pb) = (ToScreen(a), ToScreen(b));
            var line = new Line { X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y, StrokeThickness = 1.2, IsHitTestVisible = false };
            if (edge.Kind == LinkKind.Manual) line.StrokeDashArray = [3, 2];
            line.SetResourceReference(Shape.StrokeProperty, "LineBrush");
            Children.Add(line);
        }

        // Far nodes first so the center and its neighbours are drawn on top
        foreach (var node in Graph.Nodes.OrderByDescending(n => n.Hop))
        {
            if (!_layout.TryGetValue(node.Id, out var p)) continue;
            var at = ToScreen(p);
            var size = node.Hop switch { 0 => 14.0, 1 => 10.0, _ => 7.0 };
            var dot = new Ellipse
            {
                Width = size, Height = size,
                ToolTip = node.IsBroken ? $"{node.Id} (missing note)" : node.DisplayTitle,
                Cursor = node.IsBroken || node.Hop == 0 ? null : Cursors.Hand,
            };
            if (node.IsBroken)
            {
                dot.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
                dot.SetResourceReference(Shape.StrokeProperty, "ErrorBrush");
                dot.StrokeThickness = 1.5;
            }
            else
            {
                dot.SetResourceReference(Shape.FillProperty, node.Hop switch
                {
                    0 => "AccentBrush", 1 => "MutedBrush", _ => "FaintBrush",
                });
            }
            dot.MouseLeftButtonUp += (_, e) =>
            {
                if (NodeCommand?.CanExecute(node) == true) NodeCommand.Execute(node);
                e.Handled = true;
            };
            SetLeft(dot, at.X - size / 2);
            SetTop(dot, at.Y - size / 2);
            Children.Add(dot);

            if (node.Hop <= 1) AddLabel(node, at, size);
        }
    }

    private void AddLabel(GraphNode node, Point at, double size)
    {
        var text = node.DisplayTitle.Length <= LabelMaxLength ? node.DisplayTitle : node.DisplayTitle[..(LabelMaxLength - 1)] + "…";
        var label = new TextBlock { Text = text, FontSize = node.Hop == 0 ? 11 : 10, IsHitTestVisible = false };
        if (node.Hop == 0) label.FontWeight = FontWeights.SemiBold;
        label.SetResourceReference(TextBlock.ForegroundProperty, node.Hop == 0 ? "TextBrush" : "MutedBrush");
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Right of the dot, or left of it when it would run off the edge
        var left = at.X + size / 2 + 3;
        if (left + label.DesiredSize.Width > ActualWidth) left = at.X - size / 2 - 3 - label.DesiredSize.Width;
        SetLeft(label, Math.Max(0, left));
        SetTop(label, Math.Clamp(at.Y - label.DesiredSize.Height / 2, 0, Math.Max(0, ActualHeight - label.DesiredSize.Height)));
        Children.Add(label);
    }
}

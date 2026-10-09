using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Corevanta.Desktop;

public sealed class LogicVertex : INotifyPropertyChanged
{
    private string _key = string.Empty;
    private string _title = string.Empty;
    private string _kind = string.Empty;
    private string _algorithm = string.Empty;
    private Point _position;
    private double _width = 180;
    private double _height = 96;
    private double _lineHeight = double.NaN;
    private bool _isDraggable = true;
    private bool _isClProgram;
    private bool _isArrayMappingChart;
    private bool _showUpstreamIndicator;
    private bool _showDownstreamIndicator;
    private bool _isUpstreamExpanded;
    private bool _isDownstreamExpanded;

    public string Key
    {
        get => _key;
        set => SetField(ref _key, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Kind
    {
        get => _kind;
        set => SetField(ref _kind, value);
    }

    public string Algorithm
    {
        get => _algorithm;
        set => SetField(ref _algorithm, value);
    }

    public Point Position
    {
        get => _position;
        set
        {
            if (!SetField(ref _position, value))
            {
                return;
            }

            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Width
    {
        get => _width;
        set => SetField(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => SetField(ref _height, value);
    }

    public double LineHeight
    {
        get => _lineHeight;
        set => SetField(ref _lineHeight, value);
    }

    public bool IsDraggable
    {
        get => _isDraggable;
        set => SetField(ref _isDraggable, value);
    }

    public bool IsClProgram
    {
        get => _isClProgram;
        set => SetField(ref _isClProgram, value);
    }

    public bool IsArrayMappingChart
    {
        get => _isArrayMappingChart;
        set => SetField(ref _isArrayMappingChart, value);
    }

    public bool ShowUpstreamIndicator
    {
        get => _showUpstreamIndicator;
        set => SetField(ref _showUpstreamIndicator, value);
    }

    public bool ShowDownstreamIndicator
    {
        get => _showDownstreamIndicator;
        set => SetField(ref _showDownstreamIndicator, value);
    }

    public bool IsUpstreamExpanded
    {
        get => _isUpstreamExpanded;
        set
        {
            if (SetField(ref _isUpstreamExpanded, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpstreamIndicatorText)));
            }
        }
    }

    public bool IsDownstreamExpanded
    {
        get => _isDownstreamExpanded;
        set
        {
            if (SetField(ref _isDownstreamExpanded, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownstreamIndicatorText)));
            }
        }
    }

    public string UpstreamIndicatorText => IsUpstreamExpanded ? "▼" : "◀";

    public string DownstreamIndicatorText => IsDownstreamExpanded ? "▼" : "▶";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? PositionChanged;

    public override string ToString() => Title;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class LogicEdge : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string _startLabel = string.Empty;
    private string _endLabel = string.Empty;
    private bool _showText;
    private bool _showEndpointLabels;
    private int _targetSlot = -1;
    private int _targetSlotCount;
    private int _sourceSlot = -1;
    private int _sourceSlotCount;
    private bool _forceLeftTargetInput;
    private Point _sourcePoint;
    private Point _targetPoint;
    private Geometry _routeGeometry = Geometry.Empty;
    private Point _arrowPoint;
    private double _arrowAngle;
    private Point _labelPoint;
    private Point _startLabelPoint;
    private Point _endLabelPoint;

    public LogicEdge(LogicVertex source, LogicVertex target)
    {
        Source = source;
        Target = target;
        Source.PositionChanged += Vertex_PositionChanged;
        Target.PositionChanged += Vertex_PositionChanged;
        UpdateConnectionPoints();
    }

    public LogicVertex Source { get; }
    public LogicVertex Target { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
            }
        }
    }

    public bool ShowText
    {
        get => _showText;
        set
        {
            if (SetField(ref _showText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
            }
        }
    }

    public string DisplayText => ShowText ? Text : string.Empty;

    public string StartLabel
    {
        get => _startLabel;
        set
        {
            if (SetField(ref _startLabel, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayStartLabel)));
            }
        }
    }

    public string EndLabel
    {
        get => _endLabel;
        set
        {
            if (SetField(ref _endLabel, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayEndLabel)));
            }
        }
    }

    public bool ShowEndpointLabels
    {
        get => _showEndpointLabels;
        set
        {
            if (SetField(ref _showEndpointLabels, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayStartLabel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayEndLabel)));
            }
        }
    }

    public string DisplayStartLabel => ShowEndpointLabels ? (StartLabel ?? string.Empty).Trim() : string.Empty;
    public string DisplayEndLabel => ShowEndpointLabels ? (EndLabel ?? string.Empty).Trim() : string.Empty;

    public int TargetSlot
    {
        get => _targetSlot;
        set
        {
            if (SetField(ref _targetSlot, value))
            {
                UpdateConnectionPoints();
            }
        }
    }

    public int TargetSlotCount
    {
        get => _targetSlotCount;
        set
        {
            if (SetField(ref _targetSlotCount, value))
            {
                UpdateConnectionPoints();
            }
        }
    }

    public int SourceSlot
    {
        get => _sourceSlot;
        set
        {
            if (SetField(ref _sourceSlot, value))
            {
                UpdateConnectionPoints();
            }
        }
    }

    public int SourceSlotCount
    {
        get => _sourceSlotCount;
        set
        {
            if (SetField(ref _sourceSlotCount, value))
            {
                UpdateConnectionPoints();
            }
        }
    }

    public bool ForceLeftTargetInput
    {
        get => _forceLeftTargetInput;
        set
        {
            if (SetField(ref _forceLeftTargetInput, value))
            {
                UpdateConnectionPoints();
            }
        }
    }

    public Point SourcePoint
    {
        get => _sourcePoint;
        private set => SetField(ref _sourcePoint, value);
    }

    public Point TargetPoint
    {
        get => _targetPoint;
        private set => SetField(ref _targetPoint, value);
    }

    public Geometry RouteGeometry
    {
        get => _routeGeometry;
        private set => SetField(ref _routeGeometry, value);
    }

    public Point ArrowPoint
    {
        get => _arrowPoint;
        private set => SetField(ref _arrowPoint, value);
    }

    public double ArrowAngle
    {
        get => _arrowAngle;
        private set => SetField(ref _arrowAngle, value);
    }

    public Point LabelPoint
    {
        get => _labelPoint;
        private set => SetField(ref _labelPoint, value);
    }

    public Point StartLabelPoint
    {
        get => _startLabelPoint;
        private set => SetField(ref _startLabelPoint, value);
    }

    public Point EndLabelPoint
    {
        get => _endLabelPoint;
        private set => SetField(ref _endLabelPoint, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Text;

    public void UpdateConnectionPoints()
    {
        var sourceWidth = Math.Max(1, Source.Width);
        var sourceHeight = Math.Max(1, Source.Height);
        var targetHeight = Math.Max(1, Target.Height);

        var sourceY = Source.Kind.StartsWith("GATE_NOT", StringComparison.OrdinalIgnoreCase)
            ? sourceHeight / 2d
            : ComputeSideY(sourceHeight, SourceSlot, SourceSlotCount, margin: 4);
        var targetMargin = Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase) ? 4d : 8d;
        var targetY = Target.Kind.StartsWith("GATE_NOT", StringComparison.OrdinalIgnoreCase)
            ? targetHeight / 2d
            : ComputeSideY(targetHeight, TargetSlot, TargetSlotCount, margin: targetMargin);

        SourcePoint = new Point(Source.Position.X + Math.Max(0, sourceWidth - 2), Source.Position.Y + sourceY);

        var targetX = ForceLeftTargetInput
            || Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase)
            || TargetSlotCount > 1
            ? Target.Position.X + 8
            : Target.Position.X + 8;

        var targetPointY = Target.Position.Y + targetY;
        var sourceIsGate = Source.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase);
        var targetIsGate = Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase);
        if (sourceIsGate && targetIsGate && TargetSlotCount <= 1)
        {
            // Only force horizontal center-line routing when the destination gate
            // actually has a single input slot.
            var minGateY = Target.Position.Y + 6;
            var maxGateY = Target.Position.Y + Math.Max(8, targetHeight - 6);
            targetPointY = Math.Clamp(SourcePoint.Y, minGateY, maxGateY);
        }

        TargetPoint = new Point(targetX, targetPointY);

        SetRoute(BuildHorizontalPreferredRoute(SourcePoint, TargetPoint));
    }

    private void Vertex_PositionChanged(object? sender, EventArgs e) => UpdateConnectionPoints();

    private static double ComputeSideY(double height, int slot, int slotCount, double margin)
    {
        var clampedHeight = Math.Max(1, height);
        if (slotCount <= 1 || slot < 0)
        {
            return clampedHeight / 2d;
        }

        var usableHeight = Math.Max(1d, clampedHeight - (2d * margin));
        var normalized = slotCount <= 1 ? 0.5d : slot / Math.Max(1d, slotCount - 1d);
        return margin + (normalized * usableHeight);
    }

    private static IReadOnlyList<Point> BuildHorizontalPreferredRoute(in Point source, in Point target)
    {
        const double sameYThreshold = 2.0;

        // Already horizontally aligned — straight line is optimal.
        if (Math.Abs(source.Y - target.Y) <= sameYThreshold)
        {
            return [source, target];
        }

        // Forward edge (left-to-right): route as a Z-bend through the horizontal midpoint.
        // source → (midX, sourceY) → (midX, targetY) → target
        if (target.X >= source.X)
        {
            var midX = (source.X + target.X) / 2d;
            return
            [
                source,
                new Point(midX, source.Y),
                new Point(midX, target.Y),
                target,
            ];
        }

        // Backward edge (cycle / right-to-left): route above both endpoints to avoid
        // passing through the node area.
        var rightGap = source.X + 28;
        var topGap   = Math.Min(source.Y, target.Y) - 36;
        return
        [
            source,
            new Point(rightGap, source.Y),
            new Point(rightGap, topGap),
            new Point(target.X - 20, topGap),
            new Point(target.X - 20, target.Y),
            target,
        ];
    }

    public void SetRoute(IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            points = [SourcePoint, TargetPoint];
        }

        var sanitized = new List<Point>(points.Count);
        foreach (var point in points)
        {
            if (sanitized.Count == 0 || DistanceSquared(sanitized[^1], point) > 0.25)
            {
                sanitized.Add(point);
            }
        }

        if (sanitized.Count < 2)
        {
            sanitized = [SourcePoint, TargetPoint];
        }

        var geometry = BuildRoundedRouteGeometry(sanitized, cornerRadius: 10);
        geometry.Freeze();
        RouteGeometry = geometry;

        var end = sanitized[^1];
        var beforeEnd = sanitized[^2];
        ArrowPoint = end;
        ArrowAngle = Math.Atan2(end.Y - beforeEnd.Y, end.X - beforeEnd.X) * (180d / Math.PI);
        LabelPoint = ComputeMidpoint(sanitized);
        // Keep endpoint labels just inside each connected side so they remain visible in the connection canvas.
        var sourceMidX = Source.Position.X + (Source.Width / 2d);
        double startX;
        if (SourcePoint.X >= sourceMidX)
        {
            // When the source endpoint is on the right side (common for chart outputs),
            // right-anchor the label near the endpoint so it does not look offset to the left.
            var estimatedLabelWidth = Math.Max(14d, (StartLabel?.Length ?? 0) * 6.2d + 8d);
            startX = Math.Clamp(
                SourcePoint.X - estimatedLabelWidth - 2d,
                Source.Position.X + 8d,
                Source.Position.X + Math.Max(16d, Source.Width - 10d));
        }
        else
        {
            startX = Math.Clamp(
                SourcePoint.X - 56,
                Source.Position.X + 8,
                Source.Position.X + Math.Max(16, Source.Width - 76));
        }
        var startY = Math.Clamp(SourcePoint.Y - 10, Source.Position.Y + 4, Source.Position.Y + Math.Max(8, Source.Height - 20));
        StartLabelPoint = new Point(startX, startY);

        double endX;
        if (Target.Kind.Equals("LOGIC_REF", StringComparison.OrdinalIgnoreCase))
        {
            // Keep labels outside compact logic-reference nodes so they don't overlap the title band.
            var entersFromLeft = SourcePoint.X <= TargetPoint.X;
            endX = entersFromLeft
                ? Target.Position.X - 92
                : Target.Position.X + Math.Max(12, Target.Width + 8);
        }
        else if (Target.Kind.Equals("CHART", StringComparison.OrdinalIgnoreCase)
            && TargetSlotCount > 5)
        {
            // Keep chart endpoint labels outside the block to avoid text collisions
            // with node title/content when multiple references converge.
            var entersFromLeft = SourcePoint.X <= TargetPoint.X;
            endX = entersFromLeft
                ? Target.Position.X - 92
                : Target.Position.X + Math.Max(12, Target.Width + 8);
        }
        else
        {
            endX = Math.Clamp(
                TargetPoint.X + 8,
                Target.Position.X + 8,
                Target.Position.X + Math.Max(16, Target.Width - 76));
        }
        var endY = Math.Clamp(TargetPoint.Y - 10, Target.Position.Y + 4, Target.Position.Y + Math.Max(8, Target.Height - 20));
        EndLabelPoint = new Point(endX, endY);
    }

    private static PathGeometry BuildRoundedRouteGeometry(IReadOnlyList<Point> points, double cornerRadius)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        if (points.Count == 2 || cornerRadius <= 0.01)
        {
            figure.Segments.Add(new LineSegment(points[^1], true));
            return new PathGeometry([figure]);
        }

        static Vector Normalize(Vector v)
        {
            if (v.LengthSquared < 0.001)
            {
                return new Vector(0, 0);
            }

            v.Normalize();
            return v;
        }

        for (var i = 1; i < points.Count - 1; i++)
        {
            var prev = points[i - 1];
            var current = points[i];
            var next = points[i + 1];

            var inVec = Normalize(prev - current);
            var outVec = Normalize(next - current);
            if (inVec.LengthSquared < 0.001 || outVec.LengthSquared < 0.001)
            {
                continue;
            }

            var isCollinear = Math.Abs(Vector.Multiply(inVec, outVec)) > 0.999;
            if (isCollinear)
            {
                continue;
            }

            var inLength = (current - prev).Length;
            var outLength = (next - current).Length;
            var radius = Math.Min(cornerRadius, Math.Min(inLength, outLength) / 2d);
            if (radius < 0.5)
            {
                continue;
            }

            var cornerStart = current + (inVec * radius);
            var cornerEnd = current + (outVec * radius);
            figure.Segments.Add(new LineSegment(cornerStart, true));
            figure.Segments.Add(new QuadraticBezierSegment(current, cornerEnd, true));
        }

        figure.Segments.Add(new LineSegment(points[^1], true));
        return new PathGeometry([figure]);
    }

    private static Point ComputeMidpoint(IReadOnlyList<Point> points)
    {
        var total = 0d;
        for (var i = 0; i < points.Count - 1; i++)
        {
            total += Math.Sqrt(DistanceSquared(points[i], points[i + 1]));
        }

        if (total < 0.001)
        {
            return points[0];
        }

        var half = total / 2d;
        var running = 0d;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var segmentLength = Math.Sqrt(DistanceSquared(a, b));
            if (running + segmentLength >= half)
            {
                var t = (half - running) / Math.Max(0.001, segmentLength);
                return new Point(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
            }

            running += segmentLength;
        }

        return points[^1];
    }

    private static double DistanceSquared(in Point a, in Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class LogicGraph
{
    private readonly HashSet<LogicVertex> _vertices = [];
    private readonly List<LogicEdge> _edges = [];

    public IReadOnlyCollection<LogicVertex> Vertices => _vertices;
    public IReadOnlyList<LogicEdge> Edges => _edges;

    public void AddVertex(LogicVertex vertex) => _vertices.Add(vertex);

    public void AddEdge(LogicEdge edge)
    {
        _vertices.Add(edge.Source);
        _vertices.Add(edge.Target);
        _edges.Add(edge);
    }
}

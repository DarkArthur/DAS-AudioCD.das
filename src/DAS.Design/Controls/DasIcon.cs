using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Logging;
using Avalonia.Media;

namespace DAS.Design.Controls;

/// <summary>
/// Draws a DAS icon resolved from the resource <c>Das.Icon.&lt;key&gt;</c> (a <see cref="Geometry"/> on a 24x24 grid).
/// The stroke colour follows the inherited <c>Foreground</c>. An unknown key falls back to
/// <c>das.status.warning</c> and writes a warning to the Avalonia log; no exception is raised.
/// </summary>
/// <remarks>
/// The spec calls this property "Name"; it is exposed as <see cref="IconName"/> because
/// <c>StyledElement.Name</c> (x:Name) already exists on every Avalonia control.
/// </remarks>
public class DasIcon : TemplatedControl
{
    public const string FallbackKey = "das.status.warning";
    public const double DefaultSize = 24;
    public static readonly IReadOnlyList<double> AllowedSizes = [16, 20, 24, 32];

    private const double Grid = 24;
    private const double StrokeThickness = 1.5;

    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<DasIcon, string?>(nameof(IconName));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<DasIcon, double>(nameof(Size), DefaultSize, coerce: CoerceSize);

    private Geometry? _geometry;

    static DasIcon()
    {
        AffectsMeasure<DasIcon>(SizeProperty);
        AffectsRender<DasIcon>(IconNameProperty, SizeProperty, ForegroundProperty);
        IconNameProperty.Changed.AddClassHandler<DasIcon>((i, _) => i.Resolve());
    }

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    /// <summary>Icon side in px. Allowed values: 16, 20, 24, 32 (others fall back to 24).</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static double CoerceSize(AvaloniaObject o, double value)
    {
        if (AllowedSizes.Contains(value))
        {
            return value;
        }

        Logger.TryGet(LogEventLevel.Warning, "DasDesign")?.Log(o, "DasIcon.Size {Size} is not allowed (16, 20, 24, 32); using {Default}.", value, DefaultSize);
        return DefaultSize;
    }

    public DasIcon()
    {
        ActualThemeVariantChanged += (_, _) => Resolve();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Resolve();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    private void Resolve()
    {
        var name = IconName;
        Geometry? geometry = null;

        if (!string.IsNullOrWhiteSpace(name))
        {
            geometry = Find(name);
            if (geometry is null)
            {
                Logger.TryGet(LogEventLevel.Warning, "DasDesign")?.Log(this, "DasIcon: icon '{Name}' not found; using '{Fallback}'.", name, FallbackKey);
                geometry = Find(FallbackKey);
            }
        }

        _geometry = geometry;
        InvalidateVisual();
    }

    private Geometry? Find(string key)
    {
        return this.TryFindResource($"Das.Icon.{key}", ActualThemeVariant, out var res) && res is Geometry g ? g : null;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_geometry is null)
        {
            return;
        }

        var brush = Foreground;
        if (brush is null)
        {
            return;
        }

        var scale = Size / Grid;
        var pen = new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(null, pen, _geometry);
        }
    }
}



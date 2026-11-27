using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAS.Design.Controls;

public enum DasLogoForm
{
    Lockup,
    Mark,
}

/// <summary>
/// Shows the DAS_Logo from the theme resources <c>Das.Logo.Lockup</c> / <c>Das.Logo.Mark</c>
/// (light or dark variant chosen by the active theme). Proportions are always preserved.
/// </summary>
public class DasLogo : Control
{
    public static readonly StyledProperty<DasLogoForm> FormProperty =
        AvaloniaProperty.Register<DasLogo, DasLogoForm>(nameof(Form), DasLogoForm.Lockup);

    /// <summary>When true and <see cref="Form"/> is Lockup, shows the theme-independent <c>-color</c> variant.</summary>
    public static readonly StyledProperty<bool> UseColorVariantProperty =
        AvaloniaProperty.Register<DasLogo, bool>(nameof(UseColorVariant));

    public const double DefaultHeight = 32;

    private IImage? _image;

    static DasLogo()
    {
        AffectsMeasure<DasLogo>(FormProperty);
        AffectsRender<DasLogo>(FormProperty);
        FormProperty.Changed.AddClassHandler<DasLogo>((l, _) => l.Resolve());
        UseColorVariantProperty.Changed.AddClassHandler<DasLogo>((l, _) => l.Resolve());
    }

    public bool UseColorVariant
    {
        get => GetValue(UseColorVariantProperty);
        set => SetValue(UseColorVariantProperty, value);
    }

    public DasLogoForm Form
    {
        get => GetValue(FormProperty);
        set => SetValue(FormProperty, value);
    }

    public DasLogo()
    {
        ActualThemeVariantChanged += (_, _) => Resolve();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Resolve();
    }

    private void Resolve()
    {
        var key = Form == DasLogoForm.Lockup
            ? (UseColorVariant ? "Das.Logo.Lockup.Color" : "Das.Logo.Lockup")
            : "Das.Logo.Mark";
        _image = this.TryFindResource(key, ActualThemeVariant, out var res) ? res as IImage : null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_image is null || _image.Size.Height <= 0)
        {
            return default;
        }

        var h = double.IsFinite(availableSize.Height) ? availableSize.Height : DefaultHeight;
        return new Size(h * _image.Size.Width / _image.Size.Height, h);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_image is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var dest = new Rect(Bounds.Size);
        context.DrawImage(_image, new Rect(_image.Size), dest);
    }
}




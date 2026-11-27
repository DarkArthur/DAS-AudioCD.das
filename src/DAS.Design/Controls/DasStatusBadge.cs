using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace DAS.Design.Controls;

public enum DasStatus
{
    Ok,
    Warn,
    Ko,
}

/// <summary>Status badge showing icon + text + colours, so state is not conveyed by colour alone.</summary>
public class DasStatusBadge : TemplatedControl
{
    public static readonly StyledProperty<DasStatus> StatusProperty =
        AvaloniaProperty.Register<DasStatusBadge, DasStatus>(nameof(Status), DasStatus.Ok);

    public static readonly DirectProperty<DasStatusBadge, string> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<DasStatusBadge, string>(nameof(DisplayText), b => b.DisplayText);

    public static readonly DirectProperty<DasStatusBadge, string> IconNameProperty =
        AvaloniaProperty.RegisterDirect<DasStatusBadge, string>(nameof(IconName), b => b.IconName);

    private string _displayText = TextFor(DasStatus.Ok);
    private string _iconName = IconFor(DasStatus.Ok);

    public DasStatusBadge()
    {
        UpdateStatus(Status);
    }

    public DasStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string DisplayText => _displayText;

    public string IconName => _iconName;

    public static string TextFor(DasStatus status) => status switch
    {
        DasStatus.Ok => "OK",
        DasStatus.Warn => "ATTENZIONE",
        _ => "KO",
    };

    public static string IconFor(DasStatus status) => status switch
    {
        DasStatus.Ok => "das.status.ok",
        DasStatus.Warn => "das.status.warning",
        _ => "das.status.ko",
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty)
        {
            UpdateStatus(change.GetNewValue<DasStatus>());
        }
    }

    private void UpdateStatus(DasStatus status)
    {
        SetAndRaise(DisplayTextProperty, ref _displayText, TextFor(status));
        SetAndRaise(IconNameProperty, ref _iconName, IconFor(status));
        PseudoClasses.Set(":ok", status == DasStatus.Ok);
        PseudoClasses.Set(":warn", status == DasStatus.Warn);
        PseudoClasses.Set(":ko", status == DasStatus.Ko);
    }
}


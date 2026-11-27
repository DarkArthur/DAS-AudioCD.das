using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using FluentAssertions;
using NSubstitute;

namespace DiscCheck.App.Tests;

public class DiscViewModelTests
{
    private static readonly Toc SampleToc = new(
        [new TocEntry(1, 0, true), new TocEntry(2, 15000, true)], 30000);

    private static DiscReadResult Audio(AtipTime? id = null, string? reason = null) =>
        new(DiscStatus.Audio, string.Empty, SampleToc, "abc", id, id is null ? null : "Verbatim", reason);

    private static DriveViewModel _lastDrives = null!;

    private static (DiscViewModel Vm, IDiscReader Reader, IDiscMediaAssociation Assoc) Create(bool select = true)
    {
        var enumerator = Substitute.For<IDriveEnumerator>();
        var reset = Substitute.For<IDriveResetService>();
        var drives = _lastDrives = new DriveViewModel(enumerator, reset);
        if (select)
        {
            var info = new DriveInfoEx('E', "LITE-ON", "iHAS124", "1.0", true, [], 0, null, null, null, null);
            drives.SelectedDrive = new DriveItemViewModel(new DriveListItem('E', info, null));
        }

        var reader = Substitute.For<IDiscReader>();
        var assoc = Substitute.For<IDiscMediaAssociation>();
        return (new DiscViewModel(reader, drives, assoc), reader, assoc);
    }

    [Fact]
    public void Apply_Audio_ShowsTocDurationsTotalSectorsMediaIdAndManufacturer()
    {
        var (vm, _, _) = Create();
        vm.Apply(Audio(new AtipTime(97, 26, 66)));

        vm.Tracks.Select(t => t.Duration).Should().Equal("03:20", "03:20");
        vm.TotalSectors.Should().Be("30000");
        vm.MediaId.Should().Be("97:26:66");
        vm.Manufacturer.Should().Be("Verbatim");
        vm.HasMediaId.Should().BeTrue();
        vm.ScanAndVerifyAllowed.Should().BeTrue();
    }

    [Fact]
    public void Apply_AtipUnavailable_KeepsTocAndStatesReason()
    {
        var (vm, _, _) = Create();
        vm.Apply(Audio(null, "supporto non CD-R"));

        vm.Tracks.Should().HaveCount(2);
        vm.HasMediaId.Should().BeFalse();
        vm.MediaId.Should().BeEmpty();
        vm.MediaUnavailableReason.Should().Contain("non disponibili").And.Contain("supporto non CD-R");
    }

    [Theory]
    [InlineData(DiscStatus.NoDisc)]
    [InlineData(DiscStatus.NotReady)]
    [InlineData(DiscStatus.NotAudio)]
    [InlineData(DiscStatus.ReadError)]
    public void Apply_NonAudio_ClearsPreviousDiscAndShowsMessage(DiscStatus status)
    {
        var (vm, _, _) = Create();
        vm.Apply(Audio(new AtipTime(97, 26, 66)));

        vm.Apply(new DiscReadResult(status, "messaggio"));

        vm.Tracks.Should().BeEmpty();
        vm.TotalSectors.Should().BeEmpty();
        vm.MediaId.Should().BeEmpty();
        vm.Manufacturer.Should().BeEmpty();
        vm.HasDisc.Should().BeFalse();
        vm.StatusMessage.Should().Be("messaggio");
        vm.AssociateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Apply_UnsupportedMedium_DisablesScanAndVerify()
    {
        var (vm, _, _) = Create();
        vm.Apply(new DiscReadResult(DiscStatus.UnsupportedMedium, "DVD non supportato"));

        vm.ScanAndVerifyAllowed.Should().BeFalse();
        vm.StatusMessage.Should().Be("DVD non supportato");
    }

    [Fact]
    public async Task ReadDisc_UsesSelectedDriveLetter()
    {
        var (vm, reader, _) = Create();
        reader.Read('E').Returns(Audio());

        await vm.ReadDiscAsync();

        reader.Received(1).Read('E');
        vm.HasDisc.Should().BeTrue();
    }

    [Fact]
    public void ReadDisc_DisabledWithoutSelectedDrive()
    {
        var (vm, _, _) = Create(select: false);
        vm.ReadDiscCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ChangingDrive_ClearsPreviousDiscData()
    {
        var (vm, _, _) = Create();
        vm.Apply(Audio(new AtipTime(97, 26, 66)));

        var other = new DriveListItem('F', null, "non identificabile");
        _lastDrives.SelectedDrive = new DriveItemViewModel(other);

        vm.Tracks.Should().BeEmpty();
        vm.HasDisc.Should().BeFalse();
        vm.MediaId.Should().BeEmpty();
    }

    [Fact]
    public async Task Associate_CallsCoreInterfaceWithHashMediaIdAndManufacturer()
    {
        var (vm, _, assoc) = Create();
        var id = new AtipTime(97, 26, 66);
        vm.Apply(Audio(id));

        await vm.AssociateAsync();

        assoc.Received(1).Associate("abc", id, "Verbatim");
    }

    [Fact]
    public void Associate_NotAvailableWithoutMediaId()
    {
        var (vm, _, assoc) = Create();
        vm.Apply(Audio(null, "supporto non CD-R"));

        vm.AssociateCommand.CanExecute(null).Should().BeFalse();
        assoc.DidNotReceiveWithAnyArgs().Associate(default!, default!, default!);
    }
}



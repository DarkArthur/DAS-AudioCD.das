using DiscCheck.App.Startup;
using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using DiscCheck.Scsi.Drives;
using DiscCheck.Verify;
using FluentAssertions;
using NSubstitute;

namespace DiscCheck.App.Tests;

public class VerifyViewModelTests
{
    private static readonly Toc SampleToc = new([new TocEntry(1, 0, true), new TocEntry(2, 15000, true)], 30000);

    private static (VerifyViewModel Vm, IDriveRepository Repo) Create(int? readOffset = null)
    {
        var drives = new DriveViewModel(Substitute.For<IDriveEnumerator>(), Substitute.For<IDriveResetService>());
        var info = new DriveInfoEx('E', "TEST", "Drive", "1.0", true, [], 0, readOffset, null, null, null);
        drives.SelectedDrive = new DriveItemViewModel(new DriveListItem('E', info, null));
        var disc = new DiscViewModel(Substitute.For<IDiscReader>(), drives, Substitute.For<IDiscMediaAssociation>());
        disc.Apply(new DiscReadResult(DiscStatus.Audio, string.Empty, SampleToc, "abc", null, null, null));

        var transports = Substitute.For<IScsiTransportFactory>();
        var repo = Substitute.For<IDriveRepository>();
        var source = new SourceReader(Substitute.For<IFlacDecoder>());
        var offsets = new OffsetService(repo);
        var vm = new VerifyViewModel(drives, disc, new Verifier(transports, source), offsets,
                                     new OffsetAutoDetector(transports, source, offsets),
                                     new CacheDetectorRunner(transports, repo), new StartupInfo(true, true, null));
        return (vm, repo);
    }

    [Fact]
    public void Start_NeedsSourceAndOffset()
    {
        var (vm, _) = Create();
        vm.StartCommand.CanExecute(null).Should().BeFalse();

        vm.SourcePath = @"C:\music\album";
        vm.StartCommand.CanExecute(null).Should().BeFalse();
        vm.AvailabilityMessage.Should().Be(VerifyViewModel.NoOffsetMessage);
    }

    [Fact]
    public void Start_EnabledWhenSourceAndStoredOffsetPresent()
    {
        var (vm, _) = Create(readOffset: 6);
        vm.SourcePath = @"C:\music\album";

        vm.StartCommand.CanExecute(null).Should().BeTrue();
        vm.OffsetText.Should().Contain("6");
    }

    [Fact]
    public async Task ManualOffset_OutOfRange_IsRejectedAndKeepsPrevious()
    {
        var (vm, repo) = Create(readOffset: 6);
        vm.ManualOffsetText = "5001";

        await vm.SetManualOffsetAsync();

        vm.OffsetIsError.Should().BeTrue();
        vm.OffsetMessage.Should().Be(OffsetService.RangeMessage);
        vm.OffsetText.Should().Contain("6");
        repo.DidNotReceiveWithAnyArgs().AddIfMissing(default!);
    }

    [Fact]
    public async Task ManualOffset_Valid_IsSavedAndUsed()
    {
        var (vm, repo) = Create();
        vm.ManualOffsetText = "-30";

        await vm.SetManualOffsetAsync();

        vm.OffsetIsError.Should().BeFalse();
        vm.OffsetText.Should().Contain("-30");
        repo.Received(1).AddIfMissing(Arg.Is<DriveRecord>(d => d.ReadOffset == -30));
    }

    [Fact]
    public void RereadCount_IsClampedTo1To10()
    {
        var (vm, _) = Create();
        vm.MaxRereadCount = 99;
        vm.MaxRereadCount.Should().Be(10);
        vm.MaxRereadCount = 0;
        vm.MaxRereadCount.Should().Be(1);
    }
}

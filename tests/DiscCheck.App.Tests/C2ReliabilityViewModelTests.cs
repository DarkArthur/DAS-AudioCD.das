using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using DiscCheck.Scsi.Drives;
using DiscCheck.Verify;
using FluentAssertions;
using NSubstitute;

namespace DiscCheck.App.Tests;

/// <summary>Enabling of the C2_Reliability_Test start command (Requirements 13.1, 13.8).</summary>
public class C2ReliabilityViewModelTests
{
    private static readonly Toc SampleToc = new([new TocEntry(1, 0, true)], 30000);

    private static C2ReliabilityViewModel Create(bool c2Supported = true, bool withDisc = true, bool enabled = true)
    {
        var drives = new DriveViewModel(Substitute.For<IDriveEnumerator>(), Substitute.For<IDriveResetService>());
        var info = new DriveInfoEx('E', "TEST", "Drive", "1.0", c2Supported, [], 0, null, null, null, null);
        drives.SelectedDrive = new DriveItemViewModel(new DriveListItem('E', info, null));
        var disc = new DiscViewModel(Substitute.For<IDiscReader>(), drives, Substitute.For<IDiscMediaAssociation>());
        if (withDisc)
            disc.Apply(new DiscReadResult(DiscStatus.Audio, string.Empty, SampleToc, "abc", null, null, null));
        var runner = new C2ReliabilityTestRunner(Substitute.For<IScsiTransportFactory>(), Substitute.For<IDriveRepository>());
        return new C2ReliabilityViewModel(drives, disc, runner, enabled);
    }

    [Fact]
    public void Start_NeedsDeclarationAndConfirmation()
    {
        var vm = Create();
        vm.StartCommand.CanExecute(null).Should().BeFalse();
        vm.KnownDefectDeclared = true;
        vm.StartCommand.CanExecute(null).Should().BeFalse();
        vm.Confirmed = true;
        vm.StartCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Start_DriveWithoutC2_StaysDisabledWithUnsupportedMessage()
    {
        var vm = Create(c2Supported: false);
        vm.KnownDefectDeclared = true;
        vm.Confirmed = true;

        vm.StartCommand.CanExecute(null).Should().BeFalse();
        vm.AvailabilityMessage.Should().Be(C2ReliabilityTestRunner.UnsupportedMessage);
    }

    [Fact]
    public void Start_WithoutDiscOrWithoutPrivileges_StaysDisabled()
    {
        foreach (var vm in new[] { Create(withDisc: false), Create(enabled: false) })
        {
            vm.KnownDefectDeclared = true;
            vm.Confirmed = true;
            vm.StartCommand.CanExecute(null).Should().BeFalse();
            vm.HasAvailabilityMessage.Should().BeTrue();
        }
    }
}

using DiscCheck.Core;
using DiscCheck.Scsi.Drives;
using FluentAssertions;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using static DiscCheck.Scsi.Tests.FakeScsiTransport;

namespace DiscCheck.Scsi.Tests;

/// <summary>DriveEnumerator and "Reset drive" over fake transports (Requirements 4.6, 4.7, 4.9, 4.10).</summary>
public class DriveEnumerationAndResetTests
{
    private sealed class ListSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class FakeEndScan : IScanEndCommand
    {
        public string Name => "END SCAN";
        public ReadOnlyMemory<byte> Cdb { get; } = new byte[] { 0xF3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    }

    private static (ILogger Log, ListSink Sink) NewLog()
    {
        var sink = new ListSink();
        return (new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger(), sink);
    }

    private static IScsiTransportFactory FactoryOf(params (char Letter, IScsiTransport Transport)[] map)
    {
        var f = Substitute.For<IScsiTransportFactory>();
        foreach (var (letter, transport) in map)
            f.Open(letter).Returns(transport);
        return f;
    }

    private static readonly byte[] Tur = new byte[6];

    // ---- Enumeration ----

    [Fact]
    public void Enumerate_NoDrives_ReturnsEmptyListWithMessage()
    {
        var factory = Substitute.For<IScsiTransportFactory>();
        var sut = new DriveEnumerator(factory, () => []);

        var r = sut.Enumerate();

        r.Items.Should().BeEmpty();
        r.Message.Should().Be(DriveEnumerator.NoDrivesMessage);
        factory.DidNotReceiveWithAnyArgs().Open(default);
    }

    [Fact]
    public void Enumerate_InquiryFails_MarksDriveUnidentifiableAndContinues()
    {
        var bad = new FakeScsiTransport().Then(Response.Failure(new TransportError.Timeout()));
        var good = new FakeScsiTransport()
            .Then(Response.Good(LoadFixture("inquiry.bin")))
            .Then(Response.Good(LoadFixture("mode-sense-2a.bin")));
        var (log, sink) = NewLog();
        var sut = new DriveEnumerator(FactoryOf(('D', bad), ('E', good)), () => ['D', 'E'], log);

        var r = sut.Enumerate();

        r.Message.Should().BeNull();
        r.Items.Should().HaveCount(2);
        r.Items[0].Letter.Should().Be('D');
        r.Items[0].IsIdentified.Should().BeFalse();
        r.Items[0].Info.Should().BeNull();
        r.Items[0].UnidentifiableReason.Should().NotBeNullOrWhiteSpace();
        r.Items[1].Letter.Should().Be('E');
        r.Items[1].IsIdentified.Should().BeTrue();
        r.Items[1].Info!.Vendor.Should().NotBeNullOrWhiteSpace();
        sink.Events.Should().Contain(e => e.Level == LogEventLevel.Warning);
        bad.Disposed.Should().BeTrue();
    }

    // ---- Reset drive ----

    [Fact]
    public void Reset_EndScanThenTurGood_Succeeds()
    {
        var t = new FakeScsiTransport().Then(Response.Good([])).Then(Response.Good([]));
        var sut = new DriveResetService(FactoryOf(('D', t)), new FakeEndScan(), NewLog().Log);

        var r = sut.Reset('D');

        r.Success.Should().BeTrue();
        r.Message.Should().Be("Drive ripristinato");
        t.Calls.Should().HaveCount(2);
        t.Calls[0].Cdb[0].Should().Be(0xF3);
        t.Calls[1].Cdb.Should().Equal(Tur);
        t.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Reset_TurReportsNoDisc_StillSucceeds()
    {
        var t = new FakeScsiTransport().Then(Response.Good([])).Then(Response.Check(0x02, 0x3A, 0x00));
        var sut = new DriveResetService(FactoryOf(('D', t)), new FakeEndScan(), NewLog().Log);

        var r = sut.Reset('D');

        r.Success.Should().BeTrue();
        r.Message.Should().Be("Drive ripristinato");
    }

    [Fact]
    public void Reset_EndScanFails_ReportsErrorWithSenseAndLogsIt()
    {
        var t = new FakeScsiTransport().Then(Response.Check(0x05, 0x20, 0x00));
        var (log, sink) = NewLog();
        var sut = new DriveResetService(FactoryOf(('D', t)), new FakeEndScan(), log);

        var r = sut.Reset('D');

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("END SCAN").And.Contain(DriveResetService.RecoveryAdvice);
        t.Calls.Should().HaveCount(1, "TEST UNIT READY is not sent after a failed end-of-scan command");
        sink.Events.Should().Contain(e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void Reset_TurTimeout_ReportsErrorAndLogsIt()
    {
        var t = new FakeScsiTransport().Then(Response.Failure(new TransportError.Timeout()));
        var (log, sink) = NewLog();
        var sut = new DriveResetService(FactoryOf(('D', t)), endScan: null, log);

        var r = sut.Reset('D');

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("TEST UNIT READY").And.Contain(DriveResetService.RecoveryAdvice);
        sink.Events.Should().ContainSingle(e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void Reset_TurNotReady_MessageContainsSenseDescription()
    {
        var t = new FakeScsiTransport().Then(Response.Check(0x02, 0x04, 0x01));
        var sut = new DriveResetService(FactoryOf(('D', t)), endScan: null, NewLog().Log);

        var r = sut.Reset('D');

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("TEST UNIT READY");
    }
}

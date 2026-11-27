using FluentAssertions;

namespace DiscCheck.Scsi.Tests;

public class FakeScsiTransportTests
{
    private static readonly byte[] Inquiry = [0x12, 0, 0, 0, 36, 0];

    [Fact]
    public void Mapped_cdb_returns_data_and_good_status()
    {
        using var t = new FakeScsiTransport().When(Inquiry, FakeScsiTransport.Response.Good([1, 2, 3]));
        var buf = new byte[8];

        var r = t.Execute(Inquiry, buf, DataDirection.In, TimeSpan.FromSeconds(30));

        r.IsGood.Should().BeTrue();
        r.BytesTransferred.Should().Be(3);
        buf[..3].Should().Equal(1, 2, 3);
        t.Calls.Should().ContainSingle().Which.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Unknown_cdb_returns_check_condition_value_without_throwing()
    {
        using var t = new FakeScsiTransport();

        var r = t.Execute(Inquiry, new byte[4], DataDirection.In, TimeSpan.FromSeconds(1));

        r.IsGood.Should().BeFalse();
        r.Sense.Should().Be(new SenseData(0x05, 0x20, 0x00));
    }

    [Fact]
    public void Transport_errors_are_values()
    {
        using var t = new FakeScsiTransport()
            .Then(FakeScsiTransport.Response.Failure(new TransportError.Timeout()))
            .Then(FakeScsiTransport.Response.Failure(new TransportError.WinError(5)));

        t.Execute(Inquiry, [], DataDirection.None, TimeSpan.Zero).Error.Should().BeOfType<TransportError.Timeout>();
        t.Execute(Inquiry, [], DataDirection.None, TimeSpan.Zero).Error.Should().Be(new TransportError.WinError(5));
    }

    [Fact]
    public void Data_is_truncated_to_the_caller_buffer()
    {
        using var t = new FakeScsiTransport().When(Inquiry, FakeScsiTransport.Response.Good([1, 2, 3, 4]));
        var buf = new byte[2];

        t.Execute(Inquiry, buf, DataDirection.In, TimeSpan.Zero).BytesTransferred.Should().Be(2);
    }
}

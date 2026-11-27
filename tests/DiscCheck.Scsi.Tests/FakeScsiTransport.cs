namespace DiscCheck.Scsi.Tests;

/// <summary>
/// Test transport that answers from a CDB map and/or a FIFO sequence of canned responses.
/// Data payloads can be loaded from <c>tests/Fixtures/*.bin</c>. Unknown CDBs yield CHECK CONDITION
/// (ILLEGAL REQUEST, invalid command operation code) as a value, never an exception.
/// </summary>
public sealed class FakeScsiTransport : IScsiTransport
{
    public sealed record Response(ScsiResult Result, byte[] Data)
    {
        public static Response Good(byte[] data) => new(new ScsiResult(ScsiResult.StatusGood, null, data.Length, null), data);

        public static Response Check(byte key, byte asc, byte ascq) =>
            new(new ScsiResult(ScsiResult.StatusCheckCondition, new SenseData(key, asc, ascq), 0, null), []);

        public static Response Failure(TransportError error) => new(new ScsiResult(0, null, 0, error), []);
    }

    public sealed record Call(byte[] Cdb, DataDirection Direction, TimeSpan Timeout);

    private readonly Dictionary<string, Queue<Response>> _byCdb = new();
    private readonly Queue<Response> _sequence = new();
    private readonly List<Call> _calls = [];

    public IReadOnlyList<Call> Calls => _calls;

    public bool Disposed { get; private set; }

    /// <summary>Directory containing fixtures; defaults to the copied <c>Fixtures</c> output folder.</summary>
    public static string FixturesDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static byte[] LoadFixture(string fileName) => File.ReadAllBytes(Path.Combine(FixturesDirectory, fileName));

    /// <summary>Registers a response for an exact CDB. Several registrations are consumed in order; the last one repeats.</summary>
    public FakeScsiTransport When(ReadOnlySpan<byte> cdb, Response response)
    {
        var key = Convert.ToHexString(cdb);
        if (!_byCdb.TryGetValue(key, out var queue))
            _byCdb[key] = queue = new Queue<Response>();
        queue.Enqueue(response);
        return this;
    }

    /// <summary>Registers a GOOD response whose data is read from a fixture file.</summary>
    public FakeScsiTransport WhenFixture(ReadOnlySpan<byte> cdb, string fixtureFile) =>
        When(cdb, Response.Good(LoadFixture(fixtureFile)));

    /// <summary>Appends a response consumed by the next call without a CDB-specific match.</summary>
    public FakeScsiTransport Then(Response response)
    {
        _sequence.Enqueue(response);
        return this;
    }

    public ScsiResult Execute(ReadOnlySpan<byte> cdb, Span<byte> data, DataDirection direction, TimeSpan timeout)
    {
        _calls.Add(new Call(cdb.ToArray(), direction, timeout));

        var response = Next(Convert.ToHexString(cdb));
        if (response is null)
            return Response.Check(0x05, 0x20, 0x00).Result;

        if (direction == DataDirection.In && response.Data.Length > 0)
        {
            var n = Math.Min(data.Length, response.Data.Length);
            response.Data.AsSpan(0, n).CopyTo(data);
            return response.Result with { BytesTransferred = n };
        }

        return response.Result;
    }

    private Response? Next(string key)
    {
        if (_byCdb.TryGetValue(key, out var queue))
            return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        return _sequence.Count > 0 ? _sequence.Dequeue() : null;
    }

    public void Dispose() => Disposed = true;
}

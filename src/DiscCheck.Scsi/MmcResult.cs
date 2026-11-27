namespace DiscCheck.Scsi;

/// <summary>Error produced by a typed MMC command.</summary>
public abstract record MmcError
{
    private MmcError() { }

    /// <summary>The command completed with a SCSI status other than GOOD, or a transport error occurred.</summary>
    public sealed record CommandError(string Name, byte Status, SenseData? Sense, TransportError? Transport = null) : MmcError;

    /// <summary>The response is shorter than the minimum length required by the command.</summary>
    public sealed record ParseError(string Command, int MinLen, int ActualLen) : MmcError;
}

/// <summary>Either a value or an <see cref="MmcError"/>; never a partial value on error.</summary>
public readonly struct MmcResult<T>
{
    private readonly T? _value;
    private readonly MmcError? _error;

    private MmcResult(T? value, MmcError? error)
    {
        _value = value;
        _error = error;
    }

    public bool IsSuccess => _error is null;

    /// <summary>The parsed value. Throws <see cref="InvalidOperationException"/> if the result is an error.</summary>
    public T Value => _error is null
        ? _value!
        : throw new InvalidOperationException($"MMC command failed: {_error}");

    /// <summary>The error, or null on success.</summary>
    public MmcError? Error => _error;

    public static MmcResult<T> Ok(T value) => new(value, null);
    public static MmcResult<T> Fail(MmcError error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    public bool TryGetValue(out T value)
    {
        value = _error is null ? _value! : default!;
        return _error is null;
    }
}

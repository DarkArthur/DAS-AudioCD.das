namespace DiscCheck.Core;

/// <summary>
/// Outcome of the bit-perfect comparison of one track (Requirements 14.8, 14.9, 14.18, 15.9).
/// <para>
/// <see cref="DifferentSamples"/> counts stereo sample frames; <see cref="FirstDiffIndex"/> is the 0-based frame index
/// from the start of the track (drive side) of the first differing frame, null when the track is identical.
/// The CRC32/SHA-256 pairs are computed on the compared region only (the frames left after excluding
/// <see cref="ExcludedEdgeSamples"/> edge frames), so identical tracks have equal read and source hashes.
/// </para>
/// </summary>
public sealed record TrackResult(int TrackNo, bool Identical, long DifferentSamples, long? FirstDiffIndex,
                                 uint CrcRead, uint CrcSource, string Sha256Read, string Sha256Source,
                                 int ExcludedEdgeSamples, IReadOnlyList<int> UnreliableLbas);

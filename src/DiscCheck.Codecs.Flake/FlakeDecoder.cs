using CUETools.Codecs;
using CUETools.Codecs.FLAKE;
using DiscCheck.Core;

namespace DiscCheck.Codecs.Flake;

/// <summary><see cref="IFlacDecoder"/> backed by CUETools.Codecs.FLAKE (decision D1).</summary>
public sealed class FlakeDecoder : IFlacDecoder
{
    public PcmStream Decode(string path)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new SourceFileException(path, SourceFileFailure.Unreadable, inner: e);
        }

        FlakeReader? reader = null;
        try
        {
            reader = new FlakeReader(path, file);
            var pcm = reader.PCM;
            var format = new PcmFormat(pcm.SampleRate, pcm.ChannelCount, pcm.BitsPerSample);
            SourceFileException.RequireCdAudio(path, format);
            return new FlakePcmStream(path, reader, file, format);
        }
        catch (SourceFileException)
        {
            Cleanup(reader, file);
            throw;
        }
        catch (IOException e)
        {
            Cleanup(reader, file);
            throw new SourceFileException(path, SourceFileFailure.Unreadable, inner: e);
        }
        catch (Exception e)
        {
            Cleanup(reader, file);
            throw new SourceFileException(path, SourceFileFailure.NotDecodable, e.Message, e);
        }
    }

    private static void Cleanup(FlakeReader? reader, FileStream file)
    {
        try { reader?.Close(); } catch { /* best effort */ }
        file.Dispose();
    }

    private sealed class FlakePcmStream(string path, FlakeReader reader, FileStream file, PcmFormat format) : PcmStream
    {
        private const int ChunkFrames = 4096;
        private readonly AudioBuffer _buffer = new(reader.PCM, ChunkFrames);
        private int _bufferPos;
        private int _bufferLen;
        private bool _eof;

        public override PcmFormat Format => format;
        public override long TotalFrames => reader.Length;

        public override int Read(Span<short> destination)
        {
            int want = destination.Length / 2;
            int done = 0;
            try
            {
                while (done < want)
                {
                    if (_bufferPos == _bufferLen)
                    {
                        if (_eof || reader.Read(_buffer, ChunkFrames) == 0)
                        {
                            _eof = true;
                            break;
                        }

                        _bufferPos = 0;
                        _bufferLen = _buffer.Length;
                        if (_bufferLen == 0)
                        {
                            _eof = true;
                            break;
                        }
                    }

                    int n = Math.Min(want - done, _bufferLen - _bufferPos);
                    var samples = _buffer.Samples;
                    for (int i = 0; i < n; i++)
                    {
                        destination[(done + i) * 2] = (short)samples[_bufferPos + i, 0];
                        destination[(done + i) * 2 + 1] = (short)samples[_bufferPos + i, 1];
                    }

                    _bufferPos += n;
                    done += n;
                }
            }
            catch (Exception e) when (e is not SourceFileException)
            {
                throw new SourceFileException(path, SourceFileFailure.NotDecodable, e.Message, e);
            }

            return done;
        }

        public override void Dispose()
        {
            try { reader.Close(); } catch { /* best effort */ }
            file.Dispose();
        }
    }
}

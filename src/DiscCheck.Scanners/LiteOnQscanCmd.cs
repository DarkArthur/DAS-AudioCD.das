/*
 * Original file: plugins/liteon/qscan_cmd.cpp (QPxTool, github.com/speed47/qpxtool, commit 9c6a3fdb5e27)
 *
 * Original header:
 *   This file is part of the QPxTool project.
 *   Copyright (C) 2005-2006 Gennady "ShultZ" Kozlov <qpxtool@mail.ru>
 *
 *   Thanks to Artur Kalimullin (Kaliy) <kalimullin@gmail.com> for assistance in LiteOn Cx scan implementation
 *
 *   This program is free software; you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation; either version 2 of the License, or
 *   (at your option) any later version.
 *   See the file "COPYING" for the exact licensing terms.
 *
 * License: GPL-2.0 (or later, as stated by the original header).
 *
 * Modification notice: this file is a C# port of the Lite-On CD ERRC vendor commands of the original file,
 * modified by the DiscCheck project. The C++ class scan_liteon, the "dev->cmd[...]" CDB assembly and
 * "dev->cmd.transport(...)" calls were replaced by typed methods over IScsiTransport. The CDB bytes, data
 * directions and data lengths are unchanged (see docs/liteon-cdb.md). Only the CD ERRC commands are ported;
 * FE/TE, DVD, BD and HL-DT-ST commands of the original file are not.
 */

using DiscCheck.Scsi;

namespace DiscCheck.Scanners;

/// <summary>Counters returned by the Metodo_Nuovo poll (original: <c>cmd_cd_errc_block_new</c>).</summary>
/// <param name="Lba">Position M:S:F as binary integers, M*60*75 + S*75 + F (no 150 offset).</param>
/// <param name="Bler">Original field BLER (big-endian, response bytes 4-5).</param>
/// <param name="E22">Original field E22 (big-endian, response bytes 6-7).</param>
public readonly record struct LiteOnErrcNewBlock(int Lba, int Bler, int E22);

/// <summary>Counters returned by the Metodo_Vecchio read-out (original: <c>cmd_cd_errc_getdata</c>).</summary>
/// <param name="Bler">Original field BLER (big-endian, response bytes 0-1).</param>
/// <param name="E22">Original field E22 (big-endian, response bytes 2-3).</param>
/// <param name="E32">Original field E32 (one byte, response byte 4).</param>
public readonly record struct LiteOnErrcOldData(int Bler, int E22, int E32);

/// <summary>
/// Lite-On vendor CDBs for CD ERRC scanning, ported from <c>plugins/liteon/qscan_cmd.cpp</c> (class <c>scan_liteon</c>).
/// One method per original command. Every method sends exactly one CDB, never retries, and returns device
/// errors as <see cref="MmcError"/> values. Unused CDB bytes are zero, as in the original where
/// <c>dev->cmd[0] = ...</c> clears the CDB. Original CDB length is 12 bytes (<c>cmd[11] = 0x00</c> was written)
/// unless stated otherwise. Orchestration (method selection, fallback, loop, end-of-scan) is not part of this class.
/// </summary>
public static class LiteOnQscanCmd
{
    /// <summary>Data length of the old-method commands (original: 256).</summary>
    public const int OldMethodDataLength = 256;

    /// <summary>Data length of the new-method commands (original: 0x10).</summary>
    public const int NewMethodDataLength = 0x10;

    /// <summary>Minimum response length parsed for <see cref="CdErrcBlockNew"/> (fields up to byte 7).</summary>
    public const int NewBlockMinLength = 8;

    /// <summary>Minimum response length parsed for <see cref="CdErrcGetData"/> (fields up to byte 4).</summary>
    public const int OldDataMinLength = 5;

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_init_old</c>, step A (log name "LiteOn_init_A_cx_scan").
    /// CDB DF A3 00..., 12 bytes, data-in 256 bytes. The response is not interpreted.
    /// </summary>
    public static MmcResult<bool> CdErrcInitOldA(IScsiTransport transport, TimeSpan? timeout = null)
        => RunIn("LiteOn_init_A_cx_scan", transport, Cdb12(0xDF, 0xA3), OldMethodDataLength, timeout);

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_init_old</c>, step B (log name "LiteOn_init_B_cx_scan").
    /// CDB DF A0 with byte 4 = 0x02, 12 bytes, data-in 256 bytes. The response is not interpreted.
    /// </summary>
    public static MmcResult<bool> CdErrcInitOldB(IScsiTransport transport, TimeSpan? timeout = null)
        => RunIn("LiteOn_init_B_cx_scan", transport, Cdb12(0xDF, 0xA0, b4: 0x02), OldMethodDataLength, timeout);

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_init_old</c>, step C (log name "LiteOn_init_C_cx_scan").
    /// CDB DF A0 with byte 4 = 0x00, 12 bytes, data-in 256 bytes. The response is not interpreted.
    /// </summary>
    public static MmcResult<bool> CdErrcInitOldC(IScsiTransport transport, TimeSpan? timeout = null)
        => RunIn("LiteOn_init_C_cx_scan", transport, Cdb12(0xDF, 0xA0), OldMethodDataLength, timeout);

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_init_old</c>, step D (log name "LiteOn_init_D_cx_scan").
    /// CDB DF A0 with byte 4 = 0x04, 12 bytes, data-in 256 bytes. The response is not interpreted.
    /// </summary>
    public static MmcResult<bool> CdErrcInitOldD(IScsiTransport transport, TimeSpan? timeout = null)
        => RunIn("LiteOn_init_D_cx_scan", transport, Cdb12(0xDF, 0xA0, b4: 0x04), OldMethodDataLength, timeout);

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_init_old</c>, step E (log name "LiteOn_init_E_cx_scan").
    /// CDB DF A0 with byte 4 = 0x02, 12 bytes, data-in 256 bytes. The response is not interpreted.
    /// </summary>
    public static MmcResult<bool> CdErrcInitOldE(IScsiTransport transport, TimeSpan? timeout = null)
        => RunIn("LiteOn_init_E_cx_scan", transport, Cdb12(0xDF, 0xA0, b4: 0x02), OldMethodDataLength, timeout);

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_init_new</c>, second command (log name "LiteOn_errc_cd_probe NEW").
    /// CDB F3 0E, 12 bytes, data-in 16 bytes. Only success or failure is used; the response is not read.
    /// (The first command of the original function is SEEK to LBA 0: see <see cref="MmcSeekPort.Seek"/>.)
    /// </summary>
    public static MmcResult<bool> CdErrcProbeNew(IScsiTransport transport, TimeSpan? timeout = null)
        => RunIn("LiteOn_errc_cd_probe NEW", transport, Cdb12(0xF3, 0x0E), NewMethodDataLength, timeout);

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_read</c> (log name "LiteOn_cx_do_one_interval_scan").
    /// CDB DF 82 09, 12 bytes, data-in 256 bytes. Starts the measurement of one interval; the response is not interpreted.
    /// </summary>
    public static MmcResult<bool> CdErrcRead(IScsiTransport transport, TimeSpan? timeout = null)
        => RunIn("LiteOn_cx_do_one_interval_scan", transport, Cdb12(0xDF, 0x82, 0x09), OldMethodDataLength, timeout);

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_getdata</c> (log name "LiteOn_cx_do_one_interval_readout").
    /// CDB DF 82 05, 12 bytes, data-in 256 bytes. Response: BLER = bytes 0-1 (big-endian), E22 = bytes 2-3
    /// (big-endian), E32 = byte 4. A response shorter than <see cref="OldDataMinLength"/> is a <see cref="MmcError.ParseError"/>.
    /// </summary>
    public static MmcResult<LiteOnErrcOldData> CdErrcGetData(IScsiTransport transport, TimeSpan? timeout = null)
    {
        var r = Execute(transport, Cdb12(0xDF, 0x82, 0x05), OldMethodDataLength, timeout, out var data);
        if (!r.IsGood)
            return MmcResult<LiteOnErrcOldData>.Fail(ToError("LiteOn_cx_do_one_interval_readout", r));
        if (data.Length < OldDataMinLength)
            return MmcResult<LiteOnErrcOldData>.Fail(new MmcError.ParseError("LiteOn_cx_do_one_interval_readout", OldDataMinLength, data.Length));

        return MmcResult<LiteOnErrcOldData>.Ok(new LiteOnErrcOldData(
            Bler: (data[0] << 8) | data[1],
            E22: (data[2] << 8) | data[3],
            E32: data[4]));
    }

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_inteval_rst</c> (log name "LiteOn_cx_do_one_interval_rst").
    /// CDB DF 97, 12 bytes, data-in 256 bytes. Resets the interval; the response is not interpreted.
    /// </summary>
    public static MmcResult<bool> CdErrcIntervalReset(IScsiTransport transport, TimeSpan? timeout = null)
        => RunIn("LiteOn_cx_do_one_interval_rst", transport, Cdb12(0xDF, 0x97), OldMethodDataLength, timeout);

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_block_new</c> (log name "LiteOn_errc_cd_read_block").
    /// CDB F3 0E, 12 bytes, data-in 16 bytes. Response: bytes 1, 2, 3 = M, S, F as binary integers
    /// (LBA = M*60*75 + S*75 + F), BLER = bytes 4-5, E22 = bytes 6-7 (big-endian). The original sets E32 and UNCR to 0
    /// and does not read them. A response shorter than <see cref="NewBlockMinLength"/> is a <see cref="MmcError.ParseError"/>.
    /// </summary>
    public static MmcResult<LiteOnErrcNewBlock> CdErrcBlockNew(IScsiTransport transport, TimeSpan? timeout = null)
    {
        var r = Execute(transport, Cdb12(0xF3, 0x0E), NewMethodDataLength, timeout, out var data);
        if (!r.IsGood)
            return MmcResult<LiteOnErrcNewBlock>.Fail(ToError("LiteOn_errc_cd_read_block", r));
        if (data.Length < NewBlockMinLength)
            return MmcResult<LiteOnErrcNewBlock>.Fail(new MmcError.ParseError("LiteOn_errc_cd_read_block", NewBlockMinLength, data.Length));

        return MmcResult<LiteOnErrcNewBlock>.Ok(new LiteOnErrcNewBlock(
            Lba: data[1] * 60 * 75 + data[2] * 75 + data[3],
            Bler: (data[4] << 8) | data[5],
            E22: (data[6] << 8) | data[7]));
    }

    /// <summary>
    /// Original: qscan_cmd.cpp, <c>cmd_cd_errc_end</c>, branch Metodo_Vecchio (log name "LiteOn_end_scan").
    /// CDB DF A3 01, <b>10 bytes</b> (<c>cmd[11]</c> is not written), data-in 256 bytes. For Metodo_Nuovo the original
    /// sends nothing and returns 0: the caller must not call this method in that case.
    /// </summary>
    public static MmcResult<bool> CdErrcEndOld(IScsiTransport transport, TimeSpan? timeout = null)
    {
        var cdb = new byte[10];
        cdb[0] = 0xDF;
        cdb[1] = 0xA3;
        cdb[2] = 0x01;
        return RunIn("LiteOn_end_scan", transport, cdb, OldMethodDataLength, timeout);
    }

    private static byte[] Cdb12(byte b0, byte b1, byte b2 = 0, byte b4 = 0)
    {
        var cdb = new byte[12];
        cdb[0] = b0;
        cdb[1] = b1;
        cdb[2] = b2;
        cdb[4] = b4;
        return cdb;
    }

    private static ScsiResult Execute(IScsiTransport transport, byte[] cdb, int length, TimeSpan? timeout, out ReadOnlySpan<byte> data)
    {
        var buffer = new byte[length];
        var r = transport.Execute(cdb, buffer, DataDirection.In, timeout ?? MmcCommands.DefaultTimeout);
        data = buffer.AsSpan(0, Math.Clamp(r.BytesTransferred, 0, buffer.Length));
        return r;
    }

    private static MmcResult<bool> RunIn(string name, IScsiTransport transport, byte[] cdb, int length, TimeSpan? timeout)
    {
        var r = Execute(transport, cdb, length, timeout, out _);
        return r.IsGood ? MmcResult<bool>.Ok(true) : MmcResult<bool>.Fail(ToError(name, r));
    }

    internal static MmcError ToError(string name, ScsiResult r)
        => new MmcError.CommandError(name, r.ScsiStatus, r.Sense, r.Error);
}

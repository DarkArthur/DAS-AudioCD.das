/*
 * Original file: lib/qpxtransport/qpx_mmc.cpp (QPxTool, github.com/speed47/qpxtool, commit 9c6a3fdb5e27)
 *
 * Original header:
 *   This file is part of the QPxTool project.
 *   Copyright (C) 2005-2012 Gennady "ShultZ" Kozlov <qpxtool@mail.ru>
 *
 *   original version of CD-R(W) manufacturer identification code got from cdrecord, (C) Joerg Schilling
 *
 *   This program is free software; you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation; either version 2 of the License, or
 *   (at your option) any later version.
 *   See the file "COPYING" for the exact licensing terms.
 *
 * License: GPL-2.0 (or later, as stated by the original header).
 *
 * Modification notice: this file is a C# port of the function seek() of the original file (used by the Lite-On
 * Metodo_Nuovo init), modified by the DiscCheck project: the C++ drive_info/cmd.transport calls were replaced by a
 * typed method over IScsiTransport. CDB bytes, data direction and data length are unchanged (see docs/liteon-cdb.md).
 */

using DiscCheck.Scsi;

namespace DiscCheck.Scanners;

/// <summary>Port of <c>seek()</c> from <c>lib/qpxtransport/qpx_mmc.cpp</c>.</summary>
public static class MmcSeekPort
{
    /// <summary>
    /// Original: qpx_mmc.cpp, <c>int seek(drive_info* drive, uint32_t lba, unsigned char flags)</c> (log name "SEEK").
    /// CDB 2B, byte 1 = 0, bytes 2-5 = LBA big-endian, byte 9 = flags (original default 0x00), 10 bytes, no data.
    /// </summary>
    public static MmcResult<bool> Seek(IScsiTransport transport, uint lba = 0, byte flags = 0, TimeSpan? timeout = null)
    {
        var cdb = new byte[10];
        cdb[0] = 0x2B;
        cdb[2] = (byte)(lba >> 24);
        cdb[3] = (byte)(lba >> 16);
        cdb[4] = (byte)(lba >> 8);
        cdb[5] = (byte)lba;
        cdb[9] = flags;

        var r = transport.Execute(cdb, Span<byte>.Empty, DataDirection.None, timeout ?? MmcCommands.DefaultTimeout);
        return r.IsGood
            ? MmcResult<bool>.Ok(true)
            : MmcResult<bool>.Fail(LiteOnQscanCmd.ToError("SEEK", r));
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Buffers.Binary;

namespace ReadestStats.Core;

internal static class BackupReadLimits
{
    internal const long MaxArchiveBytes = 2_000_000_000;
    internal const long MaxTotalBytes = 2_000_000_000;
    internal const int MaxEntries = 20_000;

    internal static void ValidateArchiveFile(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > MaxArchiveBytes) throw new InvalidDataException("The backup file is too large.");
        var tail = new byte[checked((int)Math.Min(input.Length, 65_557))];
        if (tail.Length < 22) throw new InvalidDataException("The backup is not a valid ZIP file.");
        input.Seek(-tail.Length, SeekOrigin.End);
        input.ReadExactly(tail);
        for (var offset = tail.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset)) != 0x06054b50) continue;
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(offset + 20));
            if (offset + 22 + commentLength != tail.Length) continue;
            var entries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(offset + 10));
            if (entries == ushort.MaxValue || entries > MaxEntries) throw new InvalidDataException("The backup contains too many files.");
            var disk = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(offset + 4));
            var directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(offset + 6));
            var diskEntries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(offset + 8));
            var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset + 12));
            var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset + 16));
            var endOffset = input.Length - tail.Length + offset;
            if (disk != 0 || directoryDisk != 0 || diskEntries != entries || (long)directoryOffset + directorySize != endOffset)
                throw new InvalidDataException("Split, ZIP64, or malformed backups are not supported.");
            // Count actual central-directory records without allocating ZipArchive
            // entries. A forged EOCD count cannot bypass the entry quota.
            input.Position = directoryOffset;
            Span<byte> header = stackalloc byte[46];
            var count = 0;
            while (input.Position < endOffset)
            {
                if (++count > MaxEntries || endOffset - input.Position < header.Length)
                    throw new InvalidDataException("The backup contains too many files or an invalid directory.");
                input.ReadExactly(header);
                if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50)
                    throw new InvalidDataException("The backup directory is invalid.");
                var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..])
                    + BinaryPrimitives.ReadUInt16LittleEndian(header[30..])
                    + BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
                if (input.Position + extraLength > endOffset) throw new InvalidDataException("The backup directory is truncated.");
                input.Seek(extraLength, SeekOrigin.Current);
            }
            if (count != entries) throw new InvalidDataException("The backup file count is inconsistent.");
            return;
        }
        throw new InvalidDataException("The backup is not a valid ZIP file.");
    }

    internal static long EntryLimit(string name) => name switch
    {
        "manifest.json" => 1_000_000,
        "settings.json" or "manual-reading.json" => 64_000_000,
        "readest-stats.db" => 1_500_000_000,
        _ when name.StartsWith("covers/", StringComparison.Ordinal) => 32_000_000,
        _ => throw new InvalidDataException("The backup contains an unsupported path.")
    };

    internal static void ValidateMetadata(ZipArchive archive, IReadOnlyCollection<string> files)
    {
        if (archive.Entries.Count > MaxEntries || files.Count > MaxEntries) throw new InvalidDataException("The backup contains too many files.");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var limit = EntryLimit(entry.FullName);
            if (entry.Length > limit) throw new InvalidDataException($"The backup entry is too large: {entry.FullName}.");
            total = checked(total + entry.Length);
            if (total > MaxTotalBytes) throw new InvalidDataException("The backup is too large to inspect or restore.");
        }
    }

    internal static byte[] ReadSmallEntry(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream();
        CopyBounded(input, output, EntryLimit(entry.FullName), MaxTotalBytes);
        return output.ToArray();
    }

    internal static string HashEntry(ZipArchiveEntry entry, ref long total)
    {
        using var input = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long count = 0;
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            count += read;
            total += read;
            if (count > EntryLimit(entry.FullName) || total > MaxTotalBytes) throw new InvalidDataException("The backup exceeds its safe size limit.");
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static long CopyBounded(Stream input, Stream output, long entryLimit, long remainingTotal)
    {
        var buffer = new byte[81920];
        long count = 0;
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            count += read;
            if (count > entryLimit || count > remainingTotal) throw new InvalidDataException("The backup exceeds its safe size limit.");
            output.Write(buffer, 0, read);
        }
        return count;
    }
}

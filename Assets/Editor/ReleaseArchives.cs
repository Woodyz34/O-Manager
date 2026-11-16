using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

// Packs the Mac and Linux builds for Tools > O-Manager > Build release.
//
// Both need files marked as "allowed to run" (the Unix executable flag), which Windows does not have. A zip made by
// Windows' own tools leaves the flag out, and then the Mac says the game "is damaged" and Linux refuses to start it.
// So the archives are written here by hand, with the flag set on the game's programs:
//   - Mac:   O-Manager-Mac.zip with O-Manager.app inside (a double click in Finder unpacks it)
//   - Linux: O-Manager-Linux.tar.gz with an O-Manager folder inside
public static class ReleaseArchives
{
    const int RegularMode = 0x81A4;   // 0100644: a normal file
    const int ExecMode = 0x81ED;   // 0100755: a program
    const int DirMode = 0x41ED;    // 040755: a folder

    // ---------- Mac: zip ----------

    // appPath = ...\O-Manager.app. The zip holds "O-Manager.app/..." at the top.
    public static void ZipMacApp(string appPath, string zipPath)
    {
        string baseDir = Path.GetDirectoryName(appPath);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        var central = new List<CentralEntry>();
        using (FileStream zip = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite))
        {
            central.Add(WriteZipEntry(zip, Path.GetFileName(appPath.TrimEnd('\\', '/')) + "/", null, DirMode));
            foreach (string dir in AllDirs(appPath))
            {
                central.Add(WriteZipEntry(zip, Relative(baseDir, dir) + "/", null, DirMode));
            }
            foreach (string file in AllFiles(appPath))
            {
                string name = Relative(baseDir, file);
                central.Add(WriteZipEntry(zip, name, file, IsMacProgram(name) ? ExecMode : RegularMode));
            }

            long centralStart = zip.Position;
            var w = new BinaryWriter(zip, Encoding.UTF8, true);
            foreach (CentralEntry e in central)
            {
                w.Write(0x02014b50u);
                w.Write((ushort)((3 << 8) | 20));   // made by Unix, so the Mac reads the file modes
                w.Write((ushort)20);
                w.Write((ushort)0x0800);            // names in UTF-8
                w.Write(e.Method);
                w.Write(e.Time);
                w.Write(e.Date);
                w.Write(e.Crc);
                w.Write(e.Compressed);
                w.Write(e.Size);
                w.Write((ushort)e.Name.Length);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write(((uint)e.Mode << 16) | (e.Mode == DirMode ? 0x10u : 0u));
                w.Write(e.Offset);
                w.Write(e.Name);
            }
            long centralEnd = zip.Position;
            w.Write(0x06054b50u);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)central.Count);
            w.Write((ushort)central.Count);
            w.Write((uint)(centralEnd - centralStart));
            w.Write((uint)centralStart);
            w.Write((ushort)0);
            w.Flush();
            if (central.Count > 65535 || zip.Position > uint.MaxValue)
            {
                throw new IOException("The Mac game is too big for a simple zip (over 4 GB or 65 535 files).");
            }
        }
    }

    // Everything in Contents/MacOS is a program; libraries get the flag too (it does no harm)
    static bool IsMacProgram(string name)
    {
        return name.Contains("/Contents/MacOS/") || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
    }

    class CentralEntry
    {
        public byte[] Name;
        public ushort Method, Time, Date;
        public uint Crc, Compressed, Size, Offset;
        public int Mode;
    }

    static CentralEntry WriteZipEntry(FileStream zip, string name, string sourceFile, int mode)
    {
        DateTime stamp = sourceFile != null ? File.GetLastWriteTime(sourceFile) : DateTime.Now;
        if (stamp.Year < 1980) stamp = new DateTime(1980, 1, 1);
        var e = new CentralEntry
        {
            Name = Encoding.UTF8.GetBytes(name),
            // Empty files and folders are "stored": a deflate stream with nothing in it may be zero bytes, which unzip rejects
            Method = (ushort)(sourceFile != null && new FileInfo(sourceFile).Length > 0 ? 8 : 0),
            Time = (ushort)((stamp.Hour << 11) | (stamp.Minute << 5) | (stamp.Second / 2)),
            Date = (ushort)(((stamp.Year - 1980) << 9) | (stamp.Month << 5) | stamp.Day),
            Offset = (uint)zip.Position,
            Mode = mode,
        };

        var w = new BinaryWriter(zip, Encoding.UTF8, true);
        w.Write(0x04034b50u);
        w.Write((ushort)20);
        w.Write((ushort)0x0800);
        w.Write(e.Method);
        w.Write(e.Time);
        w.Write(e.Date);
        long sizesAt = zip.Position;
        w.Write(0u);   // crc, sizes: filled in below
        w.Write(0u);
        w.Write(0u);
        w.Write((ushort)e.Name.Length);
        w.Write((ushort)0);
        w.Write(e.Name);
        w.Flush();

        if (e.Method == 0) return e;

        long dataStart = zip.Position;
        uint crc = 0xFFFFFFFFu;
        long size = 0;
        using (FileStream input = File.OpenRead(sourceFile))
        using (var deflate = new DeflateStream(zip, CompressionMode.Compress, true))
        {
            byte[] buffer = new byte[1 << 16];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                crc = Crc32(crc, buffer, read);
                size += read;
                deflate.Write(buffer, 0, read);
            }
        }
        long end = zip.Position;
        if (size > uint.MaxValue || end - dataStart > uint.MaxValue)
        {
            throw new IOException(name + " is over 4 GB, too big for a simple zip.");
        }
        e.Crc = ~crc;
        e.Size = (uint)size;
        e.Compressed = (uint)(end - dataStart);

        zip.Position = sizesAt;
        w.Write(e.Crc);
        w.Write(e.Compressed);
        w.Write(e.Size);
        w.Flush();
        zip.Position = end;
        return e;
    }

    static uint[] crcTable;

    static uint Crc32(uint crc, byte[] data, int count)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                crcTable[n] = c;
            }
        }
        for (int i = 0; i < count; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    // ---------- Linux: tar.gz ----------

    // buildDir holds O-Manager.x86_64, UnityPlayer.so and O-Manager_Data. The archive puts them in "topFolder/".
    public static void TarGzFolder(string buildDir, string topFolder, string tarGzPath)
    {
        if (File.Exists(tarGzPath)) File.Delete(tarGzPath);
        using (FileStream file = new FileStream(tarGzPath, FileMode.Create, FileAccess.Write))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        {
            WriteTarEntry(gzip, topFolder + "/", null, DirMode);
            foreach (string dir in AllDirs(buildDir))
            {
                WriteTarEntry(gzip, topFolder + "/" + Relative(buildDir, dir) + "/", null, DirMode);
            }
            foreach (string path in AllFiles(buildDir))
            {
                string name = topFolder + "/" + Relative(buildDir, path);
                bool program = name.EndsWith(".x86_64", StringComparison.OrdinalIgnoreCase)
                               || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
                WriteTarEntry(gzip, name, path, program ? ExecMode : RegularMode);
            }
            gzip.Write(new byte[1024], 0, 1024);   // two empty blocks end the archive
        }
    }

    static void WriteTarEntry(Stream output, string name, string sourceFile, int mode)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        long size = sourceFile != null ? new FileInfo(sourceFile).Length : 0;
        long mtime = (long)((sourceFile != null ? File.GetLastWriteTimeUtc(sourceFile) : DateTime.UtcNow)
                            - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        if (nameBytes.Length > 100)
        {
            // GNU long name: an extra entry that carries the whole name
            byte[] longName = new byte[nameBytes.Length + 1];
            Array.Copy(nameBytes, longName, nameBytes.Length);
            output.Write(TarHeader(Encoding.ASCII.GetBytes("././@LongLink"), 0x1A4, longName.Length, 0, (byte)'L'), 0, 512);
            output.Write(longName, 0, longName.Length);
            Pad(output, longName.Length);
            Array.Resize(ref nameBytes, 100);
        }

        byte type = sourceFile != null ? (byte)'0' : (byte)'5';
        output.Write(TarHeader(nameBytes, mode & 0xFFF, size, mtime, type), 0, 512);
        if (sourceFile == null) return;
        using (FileStream input = File.OpenRead(sourceFile))
        {
            input.CopyTo(output, 1 << 16);
        }
        Pad(output, size);
    }

    static byte[] TarHeader(byte[] name, int mode, long size, long mtime, byte type)
    {
        byte[] h = new byte[512];
        Array.Copy(name, h, Math.Min(name.Length, 100));
        Octal(h, 100, 8, mode);
        Octal(h, 108, 8, 0);       // uid
        Octal(h, 116, 8, 0);       // gid
        Octal(h, 124, 12, size);
        Octal(h, 136, 12, mtime);
        for (int i = 148; i < 156; i++) h[i] = (byte)' ';
        h[156] = type;
        Encoding.ASCII.GetBytes("ustar\0").CopyTo(h, 257);
        h[263] = (byte)'0';
        h[264] = (byte)'0';
        int sum = h.Sum(b => b);
        Octal(h, 148, 7, sum);
        h[155] = (byte)' ';
        return h;
    }

    // Writes value as octal digits filling width-1 places, then a NUL
    static void Octal(byte[] h, int at, int width, long value)
    {
        string text = Convert.ToString(value, 8).PadLeft(width - 1, '0');
        if (text.Length > width - 1) throw new IOException("A file is too big for the tar archive.");
        Encoding.ASCII.GetBytes(text).CopyTo(h, at);
        h[at + width - 1] = 0;
    }

    static void Pad(Stream output, long size)
    {
        int rest = (int)(size % 512);
        if (rest != 0) output.Write(new byte[512 - rest], 0, 512 - rest);
    }

    // ---------- Helpers ----------

    static IEnumerable<string> AllDirs(string root)
    {
        return Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderBy(d => d, StringComparer.Ordinal);
    }

    static IEnumerable<string> AllFiles(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal);
    }

    static string Relative(string baseDir, string path)
    {
        string b = Path.GetFullPath(baseDir).TrimEnd('\\', '/');
        string p = Path.GetFullPath(path);
        return p.Substring(b.Length + 1).Replace('\\', '/');
    }
}

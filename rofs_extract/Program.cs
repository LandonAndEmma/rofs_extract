using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace rofs_extract
{
    class Header
    {
        public uint HeaderSize;
        public uint UnkAOffset;
        public uint UnkASize;
        public uint DirectoriesOffset;
        public uint DirectoriesSize;
        public uint UnkBOffset;
        public uint UnkBSize;
        public uint FilesOffset;
        public uint FilesSize;
        public uint FileDataOffset;

        public const int BaseOffset = 0x400;

        public Header(byte[] data)
        {
            if(BitConverter.ToUInt32(data, 0x00) != 0x53464F52)
            {
                throw new Exception($"Not a valid ROFS file");
            }

            HeaderSize = BitConverter.ToUInt32(data, BaseOffset + 0x00);
            UnkAOffset = BitConverter.ToUInt32(data, BaseOffset + 0x04);
            UnkASize = BitConverter.ToUInt32(data, BaseOffset + 0x08);
            DirectoriesOffset = BitConverter.ToUInt32(data, BaseOffset + 0x0C);
            DirectoriesSize = BitConverter.ToUInt32(data, BaseOffset + 0x10);
            UnkBOffset = BitConverter.ToUInt32(data, BaseOffset + 0x14);
            UnkBSize = BitConverter.ToUInt32(data, BaseOffset + 0x18);
            FilesOffset = BitConverter.ToUInt32(data, BaseOffset + 0x1C);
            FilesSize = BitConverter.ToUInt32(data, BaseOffset + 0x20);
            FileDataOffset = BitConverter.ToUInt32(data, BaseOffset + 0x24);
        }
    }

    class DirEntry
    {
        public const int FixedSize = 0x18;

        public int TableOffset;
        public int ParentDirOffset;
        public int SiblingOffset;
        public int ChildDirOffset;
        public int ChildFileOffset;
        public int Unk;
        public int NameLength;
        public string Name;
        public int TotalSize;

        public DirEntry(byte[] data, int absOffset, int tableOffset)
        {
            TableOffset = tableOffset;
            ParentDirOffset = BitConverter.ToInt32(data, absOffset + 0x00);
            SiblingOffset = BitConverter.ToInt32(data, absOffset + 0x04);
            ChildDirOffset = BitConverter.ToInt32(data, absOffset + 0x08);
            ChildFileOffset = BitConverter.ToInt32(data, absOffset + 0x0C);
            Unk = BitConverter.ToInt32(data, absOffset + 0x10);
            NameLength = BitConverter.ToInt32(data, absOffset + 0x14);

            Name = NameLength > 0 ? Encoding.Unicode.GetString(data, absOffset + 0x18, NameLength): "";

            //Align to nearest 4 bytes
            TotalSize = (FixedSize + NameLength + 3) & ~3;
        }
    }

    class FileEntry
    {
        public const int FixedSize = 0x20;

        public int TableOffset;
        public int ParentDirOffset;
        public int SiblingOffsMaybe;
        public long DataOffset;
        public long DataSize;
        public int Unk;
        public int NameLength;
        public string Name;
        public int EntrySize;

        public FileEntry(byte[] data, int absOffset, int tableOffset)
        {
            TableOffset = tableOffset;
            ParentDirOffset = BitConverter.ToInt32(data, absOffset + 0x00);
            SiblingOffsMaybe = BitConverter.ToInt32(data, absOffset + 0x04);
            DataOffset = BitConverter.ToInt64(data, absOffset + 0x08);
            DataSize = BitConverter.ToInt64(data, absOffset + 0x10);
            Unk = BitConverter.ToInt32(data, absOffset + 0x18);
            NameLength = BitConverter.ToInt32(data, absOffset + 0x1C);

            Name = NameLength > 0 ? Encoding.Unicode.GetString(data, absOffset + 0x20, NameLength) : "";

            // align aagain
            EntrySize = (FixedSize + NameLength + 3) & ~3;
        }
    }

    class Program
    {
        static Dictionary<int, DirEntry> ParseDirs(byte[] data, uint metaOffset, uint metaSize)
        {
            var dirs = new Dictionary<int, DirEntry>();
            int pos = 0;
            int absBase = Header.BaseOffset + (int)metaOffset;
            while (pos < (int)metaSize)
            {
                var d = new DirEntry(data, absBase + pos, pos);
                dirs[pos] = d;
                pos += d.TotalSize;
            }
            return dirs;
        }

        static Dictionary<int, FileEntry> ParseFiles(byte[] data, uint metaOffset, uint metaSize)
        {
            var files = new Dictionary<int, FileEntry>();
            int pos = 0;
            int baseOffs = Header.BaseOffset + (int)metaOffset;
            while (pos < (int)metaSize)
            {
                var f = new FileEntry(data, baseOffs + pos, pos);
                files[pos] = f;
                pos += f.EntrySize;
            }
            return files;
        }

        static Dictionary<int, string> BuildDirPaths(Dictionary<int, DirEntry> dirs)
        {
            var paths = new Dictionary<int, string>();

            string GetPath(int offset)
            {
                if (paths.TryGetValue(offset, out var cached))
                    return cached;

                var d = dirs[offset];

                if (d.ParentDirOffset == offset)
                {
                    paths[offset] = "";
                    return "";
                }

                string parentPath = GetPath(d.ParentDirOffset);
                string full = string.IsNullOrEmpty(parentPath) ? d.Name : parentPath + "/" + d.Name;
                paths[offset] = full;
                return full;
            }

            foreach (var offset in dirs.Keys)
                GetPath(offset);

            return paths;
        }

        static void Extract(byte[] data, string outputDir)
        {
            var header = new Header(data);

            var dirs = ParseDirs(data, header.DirectoriesOffset, header.DirectoriesSize);
            var dirPaths = BuildDirPaths(dirs);
            var files = ParseFiles(data, header.FilesOffset, header.FilesSize);

            Console.WriteLine($"\nExtracting to: {outputDir}");
            int count = 0;
            foreach (var kv in files)
            {
                var f = kv.Value;
                string parentPath = dirPaths.ContainsKey(f.ParentDirOffset) ? dirPaths[f.ParentDirOffset] : "";
                string fullPath = string.IsNullOrEmpty(parentPath) ? f.Name : parentPath + "/" + f.Name;

                string outPath = Path.Combine(outputDir, fullPath.Replace('/', Path.DirectorySeparatorChar));
                string? dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                long dataOffset = Header.BaseOffset + header.FileDataOffset + f.DataOffset;
                using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                fs.Write(data, (int)dataOffset, (int)f.DataSize);

                count++;
            }

            Console.WriteLine($"Extracted {count} files!");
        }

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: RomFSExtractor <romfs_file> [output_dir]");
                return;
            }

            string inputPath = args[0];

            if (File.Exists(inputPath))
            {
                string outDir = Path.GetDirectoryName(inputPath);
                outDir = args.Length > 1 ? Path.Combine(outDir, args[1]) : Path.Combine(outDir, "RomFS");

                byte[] data = File.ReadAllBytes(inputPath);
                Extract(data, outDir);
            }
            else
            {
                throw new Exception($"File \"{inputPath}\" does not exist.\n" +
                    $"If the path to rofs.bin contains spaces, make sure to put the path in quotes");
            }
        }
    }
}
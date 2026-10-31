using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace Reflux.ServerMonitor
{
    /// <summary>
    /// PNG encoder for the screenshots. GDI+ (Bitmap.Save) doesn't filter the rows and can't be given a compression level: a
    /// 1920x1080 result screen came out at 2.8 MB, against 1.8 MB with per-row filters at zlib level 6 and 1.7 MB at level 9.
    /// Picks the filter of each row as libpng does (smallest sum of absolute values) and deflates with SharpZipLib, which
    /// takes any zlib level (System.IO.Compression on .NET Framework stops at the level 6 equivalent).
    /// </summary>
    static class PngWriter
    {
        public static void Save(Bitmap bmp, string path, int level)
        {
            int w = bmp.Width, h = bmp.Height, stride = w * 3;
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
            file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            var ihdr = new byte[13];
            WriteBE(ihdr, 0, w); WriteBE(ihdr, 4, h);
            ihdr[8] = 8; /* Bit depth */
            ihdr[9] = 2; /* Truecolor RGB */
            Chunk(file, "IHDR", ihdr, ihdr.Length);

            using var idat = new MemoryStream();
            using (var z = new DeflaterOutputStream(idat, new Deflater(Math.Max(0, Math.Min(9, level))), 1 << 16) { IsStreamOwner = false })
            {
                var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var prev = new byte[stride];
                    var cur = new byte[stride];
                    var candidates = new byte[5][];
                    for (int f = 0; f < 5; f++) { candidates[f] = new byte[stride + 1]; candidates[f][0] = (byte)f; }
                    for (int y = 0; y < h; y++)
                    {
                        unsafe
                        {
                            byte* row = (byte*)bd.Scan0 + (long)y * bd.Stride;
                            for (int x = 0; x < stride; x += 3) { cur[x] = row[x + 2]; cur[x + 1] = row[x + 1]; cur[x + 2] = row[x]; } /* BGR -> RGB */
                        }
                        var best = Filter(cur, prev, candidates);
                        z.Write(best, 0, best.Length);
                        (prev, cur) = (cur, prev);
                    }
                }
                finally { bmp.UnlockBits(bd); }
            }
            Chunk(file, "IDAT", idat.GetBuffer(), (int)idat.Length);
            Chunk(file, "IEND", new byte[0], 0);
        }

        /// <summary>Fills the five filtered versions of a row and returns the one with the smallest sum of absolute (signed) values</summary>
        static byte[] Filter(byte[] cur, byte[] prev, byte[][] c)
        {
            int n = cur.Length;
            long bestSum = long.MaxValue;
            byte[] best = null;
            for (int f = 0; f < 5; f++)
            {
                var o = c[f];
                long sum = 0;
                for (int i = 0; i < n; i++)
                {
                    int a = i >= 3 ? cur[i - 3] : 0, b = prev[i], cc = i >= 3 ? prev[i - 3] : 0;
                    int pred = f switch
                    {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) >> 1,
                        _ => Paeth(a, b, cc),
                    };
                    byte v = (byte)(cur[i] - pred);
                    o[i + 1] = v;
                    sum += v < 128 ? v : 256 - v;
                }
                if (sum < bestSum) { bestSum = sum; best = o; }
            }
            return best;
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        static void Chunk(Stream s, string type, byte[] data, int length)
        {
            var len = new byte[4];
            WriteBE(len, 0, length);
            s.Write(len, 0, 4);
            var t = Encoding.ASCII.GetBytes(type);
            s.Write(t, 0, 4);
            s.Write(data, 0, length);
            uint crc = Crc(0xFFFFFFFF, t, 4);
            crc = Crc(crc, data, length) ^ 0xFFFFFFFF;
            var cb = new byte[4];
            WriteBE(cb, 0, (int)crc);
            s.Write(cb, 0, 4);
        }

        static readonly uint[] CrcTable = MakeCrcTable();

        static uint[] MakeCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) { c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; }
                t[n] = c;
            }
            return t;
        }

        static uint Crc(uint crc, byte[] data, int length)
        {
            for (int i = 0; i < length; i++) { crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8); }
            return crc;
        }

        static void WriteBE(byte[] b, int at, int v)
        {
            b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
        }
    }
}

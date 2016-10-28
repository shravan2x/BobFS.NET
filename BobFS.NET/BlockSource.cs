using System;

namespace BobFS.NET
{
    public abstract class BlockSource
    {
        public const int SectorSize = 512;

        public abstract void ReadSector(int sector, byte[] buffer, int bufOffset = 0);
        public abstract void WriteSector(int sector, byte[] buffer, int bufOffset = 0);

        public int Read(int offset, byte[] buffer, int bufOffset, int n)
        {
            int sector = offset / SectorSize;
            int start = offset % SectorSize;

            int end = start + n;
            if (end > SectorSize)
                end = SectorSize;

            int count = end - start;

            if (count == SectorSize)
            {
                /* whole sector */
                ReadSector(sector, buffer, bufOffset);
            }
            else if (count != 0)
            {
                byte[] data = new byte[SectorSize];
                ReadSector(sector, data);
                Buffer.BlockCopy(data, start, buffer, bufOffset, count);
            }

            return count;
        }

        public int Write(int offset, byte[] buffer, int bufOffset, int n)
        {
            int sector = offset / SectorSize;
            int start = offset % SectorSize;

            int end = start + n;
            if (end > SectorSize)
                end = SectorSize;

            int count = end - start;

            if (count == SectorSize)
            {
                /* whole sector */
                WriteSector(sector, buffer, bufOffset);
            }
            else if (count != 0)
            {
                byte[] data = new byte[SectorSize];
                ReadSector(sector, data);
                Buffer.BlockCopy(buffer, bufOffset, data, start, count);
                WriteSector(sector, data);
            }

            return count;
        }

        public int ReadAll(int offset, byte[] buffer, int bufOffset, int n)
        {
            int total = 0;

            while (n > 0)
            {
                int cnt = Read(offset, buffer, bufOffset, n);
                if (cnt <= 0)
                    return total;

                total += cnt;
                n -= cnt;
                offset += cnt;
            }

            return total;
        }

        public int WriteAll(int offset, byte[] buffer, int bufOffset, int n)
        {
            int total = 0;

            while (n > 0)
            {
                int cnt = Write(offset, buffer, bufOffset, n);
                if (cnt <= 0)
                    return total;

                total += cnt;
                n -= cnt;
                offset += cnt;
            }

            return total;
        }
    }
}

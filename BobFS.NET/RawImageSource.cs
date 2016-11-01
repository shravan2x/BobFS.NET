using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BobFS.NET
{
    public class RawImageSource : BlockSource
    {
        private readonly Dictionary<int, byte[]> _sectors;
        private string _file;

        public RawImageSource()
        {
            _file = null;
            _sectors = new Dictionary<int, byte[]>();
        }

        public RawImageSource(string file)
        {
            _file = file;
            _sectors = new Dictionary<int, byte[]>();

            PopulateSectors(File.ReadAllBytes(_file));
        }

        private void PopulateSectors(byte[] file)
        {
            if (file.Length % SectorSize != 0)
                throw new Exception("Incorrectly formatted image (Sector size mismatch).");

            for (int index = 0; index < file.Length/SectorSize; index++)
            {
                byte[] tmpBuf = new byte[SectorSize];
                Buffer.BlockCopy(file, SectorSize*index, tmpBuf, 0, SectorSize);
                _sectors[index] = tmpBuf;
            }
        }

        public override void ReadSector(int sector, byte[] buffer, int bufOffset = 0)
        {
            if (!_sectors.ContainsKey(sector))
                _sectors[sector] = new byte[SectorSize];

            Buffer.BlockCopy(_sectors[sector], 0, buffer, bufOffset, SectorSize);
        }

        public override void WriteSector(int sector, byte[] buffer, int bufOffset = 0)
        {
            if (!_sectors.ContainsKey(sector))
                _sectors[sector] = new byte[SectorSize];

            Buffer.BlockCopy(buffer, bufOffset, _sectors[sector], 0, SectorSize);
        }

        public void Save(string file)
        {
            _file = file;

            int largestSector = _sectors.Keys.Max();
            byte[] writeBuf = new byte[(largestSector + 1)*SectorSize];
            foreach (KeyValuePair<int, byte[]> sector in _sectors)
                Buffer.BlockCopy(sector.Value, 0, writeBuf, sector.Key*SectorSize, SectorSize);

            // Since BobFS images are like 10MB at most, it doesn't make sense to track and syscall a write for each modified sector (In most real world cases).
            File.WriteAllBytes(file, writeBuf);
        }
    }
}

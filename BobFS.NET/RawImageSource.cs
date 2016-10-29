using System;
using System.IO;

namespace BobFS.NET
{
    public class RawImageSource : BlockSource
    {
        private readonly string _file;
        private readonly byte[] _buffer;

        public RawImageSource(string file, bool readOnly = false)
        {
            _file = file;
            _buffer = File.ReadAllBytes(_file);

            if (_buffer.Length % SectorSize != 0)
                throw new Exception("Incorrectly formatted image (Sector size mismatch).");
        }

        public override void ReadSector(int sector, byte[] buffer, int bufOffset = 0)
        {
            Buffer.BlockCopy(_buffer, SectorSize*sector, buffer, bufOffset, SectorSize);
        }

        public override void WriteSector(int sector, byte[] buffer, int bufOffset = 0)
        {
            Buffer.BlockCopy(buffer, bufOffset, _buffer, SectorSize*sector, SectorSize);
        }

        public void Save(string file)
        {
            File.WriteAllBytes(file, _buffer);
        }
    }
}

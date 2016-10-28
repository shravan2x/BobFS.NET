using System;
using System.Text;

namespace BobFS.NET
{
    public class BobFs
    {
        public const int BlockSize = 1024;
        public const string HeaderMagic = "BOBFS439";
        public const int MaxFilesize = BlockSize*257;

        internal readonly BlockSource Source;
        private readonly bool _caching;
        private readonly byte[] _tmpBuffer;
        private readonly Superblock _superBlock;

        private BobFs(BlockSource source, bool caching = true)
        {
            Source = source;
            _caching = caching;

            _tmpBuffer = new byte[BlockSource.SectorSize];
            Source.ReadAll(0, _tmpBuffer, 0, BlockSource.SectorSize);
            _superBlock = Superblock.ReadFrom(_tmpBuffer);
        }

        /// <summary>
        /// Returns a reference to the root directory.
        /// </summary>
        public BobFsNode Root => new BobFsNode(this, _superBlock.RootInum);

        /// <summary>
        /// Creates a new file system in the given device
        /// </summary>
        public static BobFs Mkfs(BlockSource device)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Mounts the given device.
        /// </summary>
        public static BobFs Mount(BlockSource device)
        {
            BobFs fs = new BobFs(device);
            fs.CheckHeader();
            return fs;
        }

        private void CheckHeader()
        {
            if (_superBlock.Magic != HeaderMagic)
                throw new Exception("Header magic incorrect!\n");
        }

        // TODO: Add support for little endian architectures
        private class Superblock
        {
            public string Magic;
            public uint RootInum;

            public static Superblock ReadFrom(byte[] buffer, int bufOffset = 0)
            {
                Superblock superBlock = new Superblock();
                superBlock.Magic = Encoding.ASCII.GetString(buffer, bufOffset + 0, bufOffset + 8);
                superBlock.RootInum = BitConverter.ToUInt32(buffer, bufOffset + 8);
                return superBlock;
            }
        }
    }
}

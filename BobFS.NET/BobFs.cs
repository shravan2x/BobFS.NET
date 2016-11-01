using System;
using System.Collections;
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
        private Superblock _superBlock;

        private BobFs(BlockSource source, bool caching = true)
        {
            Source = source;
            _caching = caching;

            _tmpBuffer = new byte[BlockSize];
            Source.ReadAll(0, _tmpBuffer, 0, BlockSize);
            _superBlock = Superblock.ReadFrom(_tmpBuffer);
        }

        public void Format()
        {
            // Setup superblock
            Array.Clear(_tmpBuffer, 0, BlockSize);
            Encoding.ASCII.GetBytes(HeaderMagic).CopyTo(_tmpBuffer, 0);
            BitConverter.GetBytes((int) 0).CopyTo(_tmpBuffer, HeaderMagic.Length);
            Source.WriteAll(0, _tmpBuffer, 0, BlockSize);
            _superBlock = Superblock.ReadFrom(_tmpBuffer);

            // Setup block bitmap
            byte[] tmpBuffer = new byte[BlockSize];
            Source.WriteAll(BlockSize*1, tmpBuffer, 0, BlockSize);

            // Clear inode bitmap
            BitArray inodeBitmap = new BitArray(tmpBuffer);
            inodeBitmap[0] = true;
            inodeBitmap.CopyTo(tmpBuffer, 0);
            Source.WriteAll(BlockSize*2, tmpBuffer, 0, BlockSize);

            // Create root directory
            BobFsNode root = new BobFsNode(this, 0);
            root.Type = ENodeType.Directory;
            root.NumLinks = 1;
            root.Size = 0;
            root.Commit();
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
            BobFs fs = new BobFs(device);
            fs.Format();
            return fs;
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

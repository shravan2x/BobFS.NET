using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace BobFS.NET
{
    public class BobFsNode
    {
        public const int NodeSize = 16;
        
        private readonly BobFs _bobFs;
        private readonly byte[] _tmpBuffer;
        private readonly Inode _node;
        private readonly Indirects _indirect;
        private bool _nodeValid, _indirectValid;

        public uint Inum { get; }

        internal BobFsNode(BobFs bobFs, uint inum)
        {
            _bobFs = bobFs;
            Inum = inum;

            _tmpBuffer = new byte[BobFs.BlockSize];
            _node = new Inode();
            _indirect = new Indirects();

            _nodeValid = _indirectValid = false;
        }

        private Inode Node
        {
            get
            {
                if (_nodeValid)
                    return _node;

                _bobFs.Source.Read((int) (BobFs.BlockSize*3 + NodeSize*Inum), _tmpBuffer, 0, NodeSize);
                _node.ReadFrom(_tmpBuffer);
                _node.Modified = false;

                _nodeValid = true;
                return _node;
            }
        }

        private Indirects Indirect
        {
            get
            {
                if (_indirectValid)
                    return _indirect;
                
                ReadBlock((int) _node.IndirectBlock, 0, _tmpBuffer, 0, NodeSize);
                _indirect.ReadFrom(_tmpBuffer);
                _indirect.Modified = false;

                _indirectValid = true;
                return _indirect;
            }
        }

        public ENodeType Type
        {
            get { return (ENodeType) Node.Type; }
            set { Node.Type = (ushort) value; }
        }

        public ushort NumLinks
        {
            get { return Node.NumLinks; }
            set { Node.NumLinks = value; }
        }

        public uint Size
        {
            get { return Node.Size; }
            set { Node.Size = value; }
        }

        public uint DirectBlock
        {
            get { return Node.DirectBlock; }
            set { Node.DirectBlock = value; }
        }

        public uint IndirectBlock
        {
            get { return Node.IndirectBlock; }
            set { Node.IndirectBlock = value; }
        }

        public bool IsFile => (Type == ENodeType.File);

        public bool IsDirectory => (Type == ENodeType.Directory);

        public List<KeyValuePair<string, BobFsNode>> Contents
        {
            get
            {
                if (!IsDirectory)
                    throw new InvalidOperationException("Current node is not a directory.");

                List<KeyValuePair<string, BobFsNode>> contents = new List<KeyValuePair<string, BobFsNode>>();

                int runner = 0;
                DirEntry entry = new DirEntry();
                while (runner < Size)
                {
                    ReadAll(runner, _tmpBuffer, 0, BobFs.BlockSize); // BUG: doesn't work with filenames larger than a block
                    entry.ReadFrom(_tmpBuffer);
                    runner += 8 + entry.Name.Length; // Point to next inum
                    contents.Add(new KeyValuePair<string, BobFsNode>(entry.Name, new BobFsNode(_bobFs, entry.Inum)));
                }

                return contents;
            }
        }

        private uint PartBlockNum(int part)
        {
            if (part > Math.Ceiling((double) Size/BobFs.BlockSize))
                throw new ArgumentException("Part invalid.");

            return part == 0 ? DirectBlock : Indirect[part - 1];
        }

        /// <summary>
        /// Similar to BlockSource.Read, but works with blocks instead of sectors.
        /// </summary>
        private int ReadBlock(int blockNum, int blockOffset, byte[] buffer, int bufOffset, int n = BobFs.BlockSize)
        {
            int end = blockOffset + n;
            if (end > BobFs.BlockSize)
                end = BobFs.BlockSize;

            int count = end - blockOffset;
            
            return _bobFs.Source.ReadAll(BobFs.BlockSize*blockNum + blockOffset, buffer, bufOffset, count);
        }

        private int WriteBlock(int blockNum, int blockOffset, byte[] buffer, int bufOffset, int n = BobFs.BlockSize)
        {
            throw new NotImplementedException();
        }

        private int Read(int offset, byte[] buffer, int bufOffset, int n)
        {
            int part = offset / BobFs.BlockSize;
            int start = offset % BobFs.BlockSize;
            
            if (part > Size/BobFs.BlockSize)
                return 0;

            int end = start + n;
            if (end > BobFs.BlockSize)
                end = BobFs.BlockSize;

            int count = end - start;
            uint block = PartBlockNum(part);

            if (count == BobFs.BlockSize)
                ReadBlock((int) block, 0, buffer, bufOffset);
            else if (count != 0)
                ReadBlock((int) block, start, buffer, bufOffset, count);

            return count;
        }

        // add 0 support
        public int ReadAll(int offset, byte[] buffer, int bufOffset, int n)
        {
            int total = 0;

            while (n > 0)
            {
                int cnt = Read(offset, buffer, bufOffset + total, n);
                if (cnt <= 0)
                    return total;

                total += cnt;
                n -= cnt;
                offset += cnt;
            }

            return total;
        }

        public int WriteAll(int offset, byte[] buffer, int n)
        {
            throw new NotImplementedException();
        }

        private BobFsNode NewDirEntry(string name, ENodeType type)
        {
            if (!IsDirectory)
                throw new InvalidOperationException("Current node is not a directory.");

            // Get free inum
            // TODO: Can be optimized
            // TODO: Implement caching
            int freeInum = -1;
            _bobFs.Source.ReadAll(BobFs.BlockSize*2, _tmpBuffer, 0, BobFs.BlockSize);
            BitArray inodeBitmap = new BitArray(_tmpBuffer);
            for (int index = 0; index < BobFs.BlockSize*8; index++)
                if (!inodeBitmap[index])
                    freeInum = index;
            if (freeInum == -1)
                throw new Exception("No free Inode.");
            inodeBitmap[freeInum] = true;
            inodeBitmap.CopyTo(_tmpBuffer, 0);
            _bobFs.Source.ReadAll(BobFs.BlockSize*2, _tmpBuffer, 0, BobFs.BlockSize);

            // Add to directory
            DirEntry newDirEntry = new DirEntry();
            newDirEntry.Inum = (uint) freeInum;
            newDirEntry.Name = name;
            newDirEntry.WriteTo(_tmpBuffer);
            int bytesWritten = WriteAll((int) Size, _tmpBuffer, name.Length + 8);
            if (bytesWritten < 0)
                throw new Exception("Current directory is full.");

            // Create inode at inum
            BobFsNode newInode = new BobFsNode(_bobFs, (uint) freeInum);
            newInode.Type = type;
            newInode.NumLinks = 1;
            newInode.Size = 0;
            newInode.Commit();

            return newInode;
        }

        public BobFsNode NewFile(string name)
        {
            return NewDirEntry(name, ENodeType.File);
        }

        public BobFsNode NewDirectory(string name)
        {
            return NewDirEntry(name, ENodeType.Directory);
        }

        public BobFsNode FindNode(string name)
        {
            if (!IsDirectory)
                throw new InvalidOperationException("Current node is not a directory.");
            
            int runner = 0;
            DirEntry entry = new DirEntry();
            while (runner < Size)
            {
                ReadAll(runner, _tmpBuffer, 0, BobFs.BlockSize); // BUG: doesn't work with filenames larger than a block
                entry.ReadFrom(_tmpBuffer);
                runner += 8 + entry.Name.Length; // Point to next inum
                if (entry.Name == name)
                    return new BobFsNode(_bobFs, entry.Inum);
            }

            return null;
        }

        public void LinkNode(string name, BobFsNode file)
        {
            if (!IsDirectory)
                throw new InvalidOperationException("Current node is not a directory.");

            // Add to directory
            DirEntry newDirEntry = new DirEntry();
            newDirEntry.Inum = file.Inum;
            newDirEntry.Name = name;
            newDirEntry.WriteTo(_tmpBuffer);
            int bytesWritten = WriteAll((int) Size, _tmpBuffer, name.Length + 8);
            if (bytesWritten < 0)
                throw new Exception("Current directory is full.");

            // Update nlinks in node
            file.Node.NumLinks++;
            file.Commit();
        }

        public void Invalidate()
        {
            _nodeValid = false;
            _indirectValid = false;
        }

        public void Commit()
        {
            // Write inode
            if (Node.Modified)
            {
                _node.WriteTo(_tmpBuffer);
                _bobFs.Source.Write((int) (BobFs.BlockSize*3 + NodeSize*Inum), _tmpBuffer, 0, NodeSize);
            }

            // Write indirects
            if (Indirect.Modified)
            {
                WriteBlock((int) _node.IndirectBlock, 0, _tmpBuffer, 0, NodeSize);
            }
        }

        // TODO: Add support for little endian architectures
        private class Inode
        {
            private ushort _type;
            private ushort _numLinks;
            private uint _size;
            private uint _directBlock;
            private uint _indirectBlock;

            public bool Modified { get; set; }

            public Inode()
            {
                Modified = false;
            }

            public ushort Type
            {
                get { return _type; }

                set
                {
                    _type = value;
                    Modified = true;
                }
            }

            public ushort NumLinks
            {
                get { return _numLinks; }

                set
                {
                    _numLinks = value;
                    Modified = true;
                }
            }

            public uint Size
            {
                get { return _size; }

                set
                {
                    _size = value;
                    Modified = true;
                }
            }

            public uint DirectBlock
            {
                get { return _directBlock; }

                set
                {
                    _directBlock = value;
                    Modified = true;
                }
            }

            public uint IndirectBlock
            {
                get { return _indirectBlock; }

                set
                {
                    _indirectBlock = value;
                    Modified = true;
                }
            }
            
            public void ReadFrom(byte[] buffer, int bufOffset = 0)
            {
                Type = BitConverter.ToUInt16(buffer, bufOffset + 0);
                NumLinks = BitConverter.ToUInt16(buffer, bufOffset + 2);
                Size = BitConverter.ToUInt32(buffer, bufOffset + 4);
                DirectBlock = BitConverter.ToUInt32(buffer, bufOffset + 8);
                IndirectBlock = BitConverter.ToUInt32(buffer, bufOffset + 12);
            }
            
            public void WriteTo(byte[] buffer, int bufOffset = 0)
            {
                BitConverter.GetBytes(Type).CopyTo(buffer, bufOffset + 0);
                BitConverter.GetBytes(NumLinks).CopyTo(buffer, bufOffset + 2);
                BitConverter.GetBytes(Size).CopyTo(buffer, bufOffset + 4);
                BitConverter.GetBytes(DirectBlock).CopyTo(buffer, bufOffset + 8);
                BitConverter.GetBytes(IndirectBlock).CopyTo(buffer, bufOffset + 12);
            }
        }

        // TODO: Add support for little endian architectures
        private class Indirects
        {
            private readonly uint[] _indirects;
            public bool Modified { get; set; }

            public Indirects()
            {
                _indirects = new uint[BobFs.BlockSize/4];
                Modified = false;
            }

            public uint this[int index]
            {
                get { return _indirects[index]; }

                set
                {
                    _indirects[index] = value;
                    Modified = true;
                }
            }

            public void ReadFrom(byte[] buffer, int bufOffset = 0)
            {
                if (buffer.Length < BobFs.BlockSize)
                    throw new Exception("Array too small.");

                for (int index = 0; index < _indirects.Length; index++)
                    this[index] = BitConverter.ToUInt32(buffer, bufOffset + index*4);
            }
            
            public void WriteTo(byte[] buffer, int bufOffset = 0)
            {
                for (int index = 0; index < _indirects.Length; index++)
                    BitConverter.GetBytes(this[index]).CopyTo(buffer, bufOffset + index*4);
            }
        }

        // TODO: Add support for little endian architectures
        private class DirEntry
        {
            public uint Inum { get; set; }
            public string Name { get; set; }
            
            public void ReadFrom(byte[] buffer, int bufOffset = 0)
            {
                Inum = BitConverter.ToUInt32(buffer, bufOffset + 0);
                uint nameLength = BitConverter.ToUInt32(buffer, bufOffset + 4);
                Name = Encoding.ASCII.GetString(buffer, bufOffset + 8, (int) nameLength);
            }
            
            public void WriteTo(byte[] buffer, int bufOffset = 0)
            {
                BitConverter.GetBytes(Inum).CopyTo(buffer, bufOffset + 0);
                BitConverter.GetBytes((uint) Name.Length).CopyTo(buffer, bufOffset + 4);
                Encoding.ASCII.GetBytes(Name).CopyTo(buffer, bufOffset + 8);
            }
        }
    }
}

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

                if (IndirectBlock == 0)
                    throw new Exception("Indirect block does not exist.");
                
                ReadBlock((int) _node.IndirectBlock, 0, _tmpBuffer, 0);
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

            set
            {
                Node.IndirectBlock = value;
                _indirectValid = false;
            }
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
                    entry.ReadFrom(this, runner);
                    runner += 8 + entry.Name.Length; // Point to next direntry's inum
                    contents.Add(new KeyValuePair<string, BobFsNode>(entry.Name, new BobFsNode(_bobFs, entry.Inum)));
                }

                return contents;
            }
        }

        private uint PartBlockNum(int part)
        {
            if (part > Math.Ceiling((double) Size/BobFs.BlockSize))
                throw new ArgumentException("Part invalid.");

            if (part > 0 && IndirectBlock == 0)
                return 0;

            return part == 0 ? DirectBlock : Indirect[part - 1];
        }

        private int FindFreeBlock()
        {
            int freeIndex = -1;

            _bobFs.Source.ReadAll(BobFs.BlockSize*1, _tmpBuffer, 0, BobFs.BlockSize);
            BitArray blockBitmap = new BitArray(_tmpBuffer);
            for (int index = 0; index < BobFs.BlockSize*8 && freeIndex == -1; index++)
                if (!blockBitmap[index])
                    freeIndex = index;

            if (freeIndex == -1)
                return -1;

            blockBitmap[freeIndex] = true;
            blockBitmap.CopyTo(_tmpBuffer, 0);
            _bobFs.Source.WriteAll(BobFs.BlockSize*1, _tmpBuffer, 0, BobFs.BlockSize);
            
            return freeIndex + 128 + 3;
        }

        private uint AssignBlock(int part)
        {
            if (part >= 1 + 256)
                throw new ArgumentException("Invalid part.");

            if (part == 0)
            {
                DirectBlock = (uint) FindFreeBlock();
                return DirectBlock;
            }

            if (IndirectBlock == 0)
                IndirectBlock = (uint) FindFreeBlock();

            if (Indirect[part - 1] != 0)
                throw new Exception("Part already assigned.");

            return (Indirect[part - 1] = (uint) FindFreeBlock());
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
            int end = blockOffset + n;
            if (end > BobFs.BlockSize)
                end = BobFs.BlockSize;

            int count = end - blockOffset;

            return _bobFs.Source.WriteAll(BobFs.BlockSize*blockNum + blockOffset, buffer, bufOffset, count);
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

        private int Write(int offset, byte[] buffer, int bufOffset, int n)
        {
            int part = offset / BobFs.BlockSize;
            int start = offset % BobFs.BlockSize;

            // Increase size if needed
            if (offset + n > Size)
                Size = (uint) (offset + n);

            // Assign block if needed
            uint block = PartBlockNum(part);
            if (block == 0)
                block = AssignBlock(part);

            int end = start + n;
            if (end > BobFs.BlockSize)
                end = BobFs.BlockSize;

            int count = end - start;

            if (count == BobFs.BlockSize)
                WriteBlock((int) block, 0, buffer, bufOffset);
            else if (count != 0)
                WriteBlock((int) block, start, buffer, bufOffset, count);

            return count;
        }

        // add 0 support
        public int ReadAll(int offset, byte[] buffer, int bufOffset, int n)
        {
            if (offset + n > Size)
                n = (int) (Size - offset); // TODO: Don't modify n

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

        public int WriteAll(int offset, byte[] buffer, int bufOffset, int n)
        {
            int total = 0;

            while (n > 0)
            {
                int cnt = Write(offset, buffer, bufOffset + total, n);
                if (cnt <= 0)
                    return total;

                total += cnt;
                n -= cnt;
                offset += cnt;
            }

            return total;
        }

        private BobFsNode NewNode(string name, ENodeType type)
        {
            if (!IsDirectory)
                throw new InvalidOperationException("Current node is not a directory.");

            // Get free inum
            // TODO: Can be optimized
            // TODO: Implement caching
            int freeInum = -1;
            _bobFs.Source.ReadAll(BobFs.BlockSize*2, _tmpBuffer, 0, BobFs.BlockSize);
            BitArray inodeBitmap = new BitArray(_tmpBuffer);
            for (int index = 0; index < BobFs.BlockSize*8 && freeInum == -1; index++)
                if (!inodeBitmap[index])
                    freeInum = index;
            if (freeInum == -1)
                throw new Exception("No free Inode.");
            inodeBitmap[freeInum] = true;
            inodeBitmap.CopyTo(_tmpBuffer, 0);
            _bobFs.Source.WriteAll(BobFs.BlockSize*2, _tmpBuffer, 0, BobFs.BlockSize);

            // Add to directory
            DirEntry newDirEntry = new DirEntry();
            newDirEntry.Inum = (uint) freeInum;
            newDirEntry.Name = name;
            int bytesWritten = newDirEntry.WriteTo(this, (int) Size);
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
            return NewNode(name, ENodeType.File);
        }

        public BobFsNode NewDirectory(string name)
        {
            return NewNode(name, ENodeType.Directory);
        }

        public BobFsNode FindNode(string name)
        {
            if (!IsDirectory)
                throw new InvalidOperationException("Current node is not a directory.");
            
            int runner = 0;
            DirEntry entry = new DirEntry();
            while (runner < Size)
            {
                entry.ReadFrom(this, runner);
                runner += 8 + entry.Name.Length; // Point to next inum
                if (entry.Name == name)
                    return new BobFsNode(_bobFs, entry.Inum);
            }

            return null;
        }

        // TODO: We should update the inode bitmap if necessary
        public void DeleteNode(BobFsNode node)
        {
            if (!IsDirectory)
                throw new InvalidOperationException("Current node is not a directory.");

            int runner = 0;
            DirEntry entry = new DirEntry();
            while (runner < Size)
            {
                entry.ReadFrom(this, runner);

                if (entry.Inum == node.Inum)
                {
                    int read;
                    while ((read = ReadAll(runner + (8 + entry.Name.Length), _tmpBuffer, 0, BobFs.BlockSize)) != 0)
                    {
                        WriteAll(runner, _tmpBuffer, 0, read);
                        runner += read;
                    }

                    Size -= (uint) (8 + entry.Name.Length);
                    return;
                }
                
                runner += 8 + entry.Name.Length; // Point to next direntry's inum
            }

            throw new Exception("Node not found.");
        }

        public void RenameNode(BobFsNode node, string name)
        {
            DeleteNode(node);
            LinkNode(name, node);
            node.NumLinks--;
            node.Commit();
        }

        public void LinkNode(string name, BobFsNode file)
        {
            if (!IsDirectory)
                throw new InvalidOperationException("Current node is not a directory.");

            // Add to directory
            DirEntry newDirEntry = new DirEntry();
            newDirEntry.Inum = file.Inum;
            newDirEntry.Name = name;
            int bytesWritten = newDirEntry.WriteTo(this, (int) Size);
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
            if (IndirectBlock != 0 && Indirect.Modified)
            {
                _indirect.WriteTo(_tmpBuffer);
                WriteBlock((int) _node.IndirectBlock, 0, _tmpBuffer, 0);
            }
        }
        
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
        
        private class DirEntry
        {
            public uint Inum { get; set; }
            public string Name { get; set; }
            
            public void ReadFrom(BobFsNode node, int offset)
            {
                byte[] headBuf = new byte[8];
                node.ReadAll(offset, headBuf, 0, 8);
                Inum = BitConverter.ToUInt32(headBuf, 0);
                uint nameLength = BitConverter.ToUInt32(headBuf, 4);

                byte[] nameBuf = new byte[nameLength];
                node.ReadAll(offset + 8, nameBuf, 0, (int) nameLength);
                Name = Encoding.ASCII.GetString(nameBuf, 0, (int) nameLength);
            }
            
            public int WriteTo(BobFsNode node, int offset)
            {
                byte[] buf = new byte[Name.Length + 8];
                BitConverter.GetBytes(Inum).CopyTo(buf, 0);
                BitConverter.GetBytes((uint) Name.Length).CopyTo(buf, 4);
                Encoding.ASCII.GetBytes(Name).CopyTo(buf, 8);
                return node.WriteAll(offset, buf, 0, buf.Length);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Buffers.Binary;

namespace LibraDBSharp
{
    public class Item
    {
        public byte[] Key { get; set; }
        public byte[] Value { get; set; }

        public Item(byte[] key, byte[] value)
        {
            Key = key;
            Value = value;
        }
    }

    public class Node
    {
        // Reference to associated transaction - not yet implemented
        public Tx Tx { get; set; }

        public ulong PageNum { get; set; }
        public List<Item> Items { get; } = new List<Item>();
        public List<ulong> ChildNodes { get; } = new List<ulong>();

        public bool IsLeaf => ChildNodes.Count == 0;

        public byte[] Serialize(byte[] buffer)
        {
            int left = 0;
            int right = buffer.Length - 1;

            buffer[left] = (byte)(IsLeaf ? 1 : 0);
            left += 1;

            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(left), (ushort)Items.Count);
            left += 2;

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (!IsLeaf)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(left), ChildNodes[i]);
                    left += sizeof(ulong);
                }

                int klen = item.Key.Length;
                int vlen = item.Value.Length;
                int offset = right - klen - vlen - 2;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(left), (ushort)offset);
                left += 2;

                right -= vlen;
                item.Value.CopyTo(buffer, right);
                right -= 1;
                buffer[right] = (byte)vlen;

                right -= klen;
                item.Key.CopyTo(buffer, right);
                right -= 1;
                buffer[right] = (byte)klen;
            }

            if (!IsLeaf)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(left), ChildNodes[ChildNodes.Count - 1]);
            }

            return buffer;
        }

        public void Deserialize(byte[] buffer)
        {
            int left = 0;
            bool isLeaf = buffer[0] == 1;
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(1));
            left += 3;

            Items.Clear();
            ChildNodes.Clear();

            for (int i = 0; i < itemCount; i++)
            {
                if (!isLeaf)
                {
                    ulong page = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(left));
                    left += sizeof(ulong);
                    ChildNodes.Add(page);
                }

                ushort offset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(left));
                left += 2;
                int klen = buffer[offset];
                offset += 1;
                byte[] key = new byte[klen];
                Array.Copy(buffer, offset, key, 0, klen);
                offset += klen;
                int vlen = buffer[offset];
                offset += 1;
                byte[] value = new byte[vlen];
                Array.Copy(buffer, offset, value, 0, vlen);
                Items.Add(new Item(key, value));
            }

            if (!isLeaf)
            {
                ulong page = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(left));
                ChildNodes.Add(page);
            }
        }
    }
}

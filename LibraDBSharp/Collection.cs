using System.Collections.Generic;

namespace LibraDBSharp
{
    public class Collection
    {
        public string Name { get; set; }
        public ulong Root { get; set; }
        internal Tx Tx { get; set; }

        private Dictionary<string, byte[]> _data = new Dictionary<string, byte[]>();

        public void Put(byte[] key, byte[] value)
        {
            _data[System.Text.Encoding.UTF8.GetString(key)] = value;
        }

        public byte[] Find(byte[] key)
        {
            _data.TryGetValue(System.Text.Encoding.UTF8.GetString(key), out var val);
            return val;
        }

        public void Remove(byte[] key)
        {
            _data.Remove(System.Text.Encoding.UTF8.GetString(key));
        }
    }
}

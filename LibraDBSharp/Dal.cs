using System.Collections.Generic;

namespace LibraDBSharp
{
    public class Dal
    {
        public int PageSize { get; private set; }
        public Meta Meta { get; private set; } = new Meta();
        public Freelist Freelist { get; private set; } = new Freelist();

        private readonly Dictionary<ulong, byte[]> _pages = new Dictionary<ulong, byte[]>();

        private Dal() {}

        public static Dal Open(string path, Options options)
        {
            var dal = new Dal
            {
                PageSize = options.PageSize
            };
            return dal;
        }

        public ulong GetNextPage() => Freelist.GetNextPage();

        public Node GetNode(ulong pageNum)
        {
            if (!_pages.TryGetValue(pageNum, out var data))
                return new Node();
            var node = new Node();
            node.Deserialize(data);
            node.PageNum = pageNum;
            return node;
        }

        public void WriteNode(Node node)
        {
            var buf = new byte[PageSize];
            node.Serialize(buf);
            _pages[node.PageNum] = buf;
        }

        public void DeleteNode(ulong pageNum)
        {
            _pages.Remove(pageNum);
            Freelist.ReleasePage(pageNum);
        }

        public void WriteFreelist() { /* no-op in memory */ }
    }
}

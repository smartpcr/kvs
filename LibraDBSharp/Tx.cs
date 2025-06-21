using System;
using System.Collections.Generic;

namespace LibraDBSharp
{
    public class Tx
    {
        internal Dictionary<ulong, Node> DirtyNodes = new Dictionary<ulong, Node>();
        internal List<ulong> PagesToDelete = new List<ulong>();
        internal List<ulong> AllocatedPages = new List<ulong>();

        public bool Write { get; }
        public DB Db { get; }

        public Tx(DB db, bool write)
        {
            Db = db;
            Write = write;
        }

        public Node NewNode(List<Item> items, List<ulong> children)
        {
            var node = new Node
            {
                PageNum = Db.GetNextPage(),
                Tx = this
            };
            node.Items.AddRange(items);
            node.ChildNodes.AddRange(children);
            AllocatedPages.Add(node.PageNum);
            DirtyNodes[node.PageNum] = node;
            return node;
        }

        public Node GetNode(ulong pageNum)
        {
            if (DirtyNodes.TryGetValue(pageNum, out var node))
                return node;
            node = Db.GetNode(pageNum);
            node.Tx = this;
            return node;
        }

        public void Rollback()
        {
            DirtyNodes.Clear();
            PagesToDelete.Clear();
            foreach (var p in AllocatedPages)
                Db.Freelist.ReleasePage(p);
            AllocatedPages.Clear();
        }

        public void Commit()
        {
            foreach (var node in DirtyNodes.Values)
            {
                Db.WriteNode(node);
            }
            foreach (var p in PagesToDelete)
                Db.DeleteNode(p);
            Db.WriteFreelist();
            DirtyNodes.Clear();
            PagesToDelete.Clear();
            AllocatedPages.Clear();
        }
    }
}

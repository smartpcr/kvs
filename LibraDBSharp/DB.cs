using System.Threading;

namespace LibraDBSharp
{
    public class DB
    {
        private ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        internal Dal Dal { get; }
        internal Freelist Freelist => Dal.Freelist;
        internal ulong Root => Dal.Meta.Root;

        public DB(Dal dal)
        {
            Dal = dal;
        }

        public static DB Open(string path, Options options)
        {
            options.PageSize = Environment.SystemPageSize;
            var dal = Dal.Open(path, options);
            return new DB(dal);
        }

        public Tx ReadTx()
        {
            _lock.EnterReadLock();
            return new Tx(this, false);
        }

        public Tx WriteTx()
        {
            _lock.EnterWriteLock();
            return new Tx(this, true);
        }

        internal ulong GetNextPage() => Dal.GetNextPage();
        internal Node GetNode(ulong pageNum) => Dal.GetNode(pageNum);
        internal void WriteNode(Node n) => Dal.WriteNode(n);
        internal void DeleteNode(ulong pg) => Dal.DeleteNode(pg);
        internal void WriteFreelist() => Dal.WriteFreelist();
    }
}

using System.Collections.Generic;

namespace LibraDBSharp
{
    public class Freelist
    {
        private ulong _maxPage;
        private Stack<ulong> _released = new Stack<ulong>();

        public ulong GetNextPage()
        {
            if (_released.Count > 0)
                return _released.Pop();
            _maxPage++;
            return _maxPage;
        }

        public void ReleasePage(ulong pg)
        {
            _released.Push(pg);
        }
    }
}

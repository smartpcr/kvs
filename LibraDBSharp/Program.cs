using System;

namespace LibraDBSharp
{
    public static class Program
    {
        public static void Example()
        {
            var db = DB.Open("libra.db", new Options());
            var tx = db.WriteTx();
            var collection = new Collection { Name = "test", Tx = tx };
            collection.Put(System.Text.Encoding.UTF8.GetBytes("key1"), System.Text.Encoding.UTF8.GetBytes("value1"));
            tx.Commit();
        }
    }
}

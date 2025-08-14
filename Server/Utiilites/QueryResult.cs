using System.Dynamic;

namespace Server.Utiilites
{
    public class QueryResult
    {
        public List<ExpandoObject> Items { get; set; } = new();
        public int TotalReturned { get; set; }
        public int Limit { get; set; }
        public bool HitLimit { get; set; }
    }
}

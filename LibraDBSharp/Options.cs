namespace LibraDBSharp
{
    public class Options
    {
        public int PageSize { get; set; }
        public float MinFillPercent { get; set; } = 0.5f;
        public float MaxFillPercent { get; set; } = 0.95f;
    }
}

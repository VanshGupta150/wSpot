namespace wSpot.Models
{
    public class AppEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }
}

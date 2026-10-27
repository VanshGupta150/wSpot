namespace wSpot.Models
{
    /// <summary>
    /// A single searchable entry — right now always an installed app,
    /// though this same shape will hold file results once Everything
    /// integration is added in Week 8.
    /// </summary>
    public class AppEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }
}

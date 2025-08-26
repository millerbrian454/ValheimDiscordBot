namespace ValheimDiscordBot.Contracts
{
    public class ProcessInfo
    {
        public int ProcessId { get; set; }
        public DateTime StartTime { get; set; }
        public long WorkingSet { get; set; }
        public string ProcessName { get; set; } = string.Empty;
    }
}

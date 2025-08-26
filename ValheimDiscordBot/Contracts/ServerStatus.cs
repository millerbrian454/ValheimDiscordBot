using ValheimDiscordBot.Services;

namespace ValheimDiscordBot.Contracts
{
    public class ServerStatus
    {
        public bool IsOnline { get; set; }
        public bool ProcessRunning { get; set; }
        public bool PortOpen { get; set; }
        public int? ResponseTime { get; set; }
        public DateTime LastChecked { get; set; }
        public string Message { get; set; } = string.Empty;
        public ProcessInfo? ProcessInfo { get; set; }
    }
}

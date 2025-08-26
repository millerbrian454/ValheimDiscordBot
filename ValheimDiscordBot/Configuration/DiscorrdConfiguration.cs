namespace ValheimDiscordBot.Configuration
{
    public class DiscordConfiguration
    {
        public const string SectionName = "Discord";

        public string BotToken { get; set; } = string.Empty;
        public string ChannelName { get; set; } = "general";
        public ulong? ChannelId { get; set; }
        public ulong? GuildId { get; set; }
        public int PostIntervalMinutes { get; set; } = 60;
        public bool Enabled { get; set; } = true;
        public AutomatedResponseSettings AutomatedResponse { get; set; } = new();
    }

    public class AutomatedResponseSettings
    {
        public bool Enabled { get; set; } = true;
        public string DefaultMessage { get; set; } = "🏔️ **Valheim Server Bot** 🏔️\n\nI'm here to help you with Valheim server information!\n\nAvailable commands:\n• `@ValheimBot status` - Get server status\n• `@ValheimBot help` - Show this help message\n• `@ValheimBot info` - Get server information";
        public Dictionary<string, string> CommandResponses { get; set; } = new()
        {
            { "status", "🟢 **Server Status**: Online and ready for Vikings!\n📊 Players: 2/10\n🌍 World: Midgard\n📍 Server: valheim.example.com:2456" },
            { "help", "🏔️ **Valheim Server Commands** 🏔️\n\n• `status` - Check server status\n• `info` - Get server details\n• `help` - Show this message" },
            { "info", "⚔️ **Valheim Server Information** ⚔️\n\n🏷️ **Name**: Epic Valheim Adventure\n🌍 **World**: Midgard\n👥 **Max Players**: 10\n🔒 **Password Protected**: Yes\n📡 **IP**: valheim.example.com:2456" }
        };
    }
}
using Discord;
using Discord.WebSocket;
using ValheimDiscordBot.Configuration;

namespace ValheimDiscordBot.Services
{
    public class DiscordBotService : IDisposable
    {
        private readonly DiscordSocketClient _client;
        private readonly ILogger<DiscordBotService> _logger;
        private readonly DiscordConfiguration _config;
        private IMessageChannel? _targetChannel;
        private bool _isReady = false;

        public DiscordBotService(ILogger<DiscordBotService> logger, DiscordConfiguration config)
        {
            _logger = logger;
            _config = config;

            var clientConfig = new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Info,
                MessageCacheSize = 100,
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            };

            _client = new DiscordSocketClient(clientConfig);

            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.Disconnected += DisconnectedAsync;
            _client.MessageReceived += MessageReceivedAsync;
        }

        public async Task<bool> StartAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.BotToken))
            {
                _logger.LogError("Discord bot token is not configured");
                return false;
            }

            try
            {
                await _client.LoginAsync(TokenType.Bot, _config.BotToken);
                await _client.StartAsync();

                // Wait for the bot to be ready
                var timeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.UtcNow;

                while (!_isReady && DateTime.UtcNow - startTime < timeout)
                {
                    await Task.Delay(500);
                }

                if (!_isReady)
                {
                    _logger.LogError("Bot failed to become ready within timeout period");
                    return false;
                }

                return await InitializeChannelAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Discord bot");
                return false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
                _logger.LogInformation("Discord bot stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Discord bot");
            }
        }

        public async Task<bool> SendMessageAsync(string message)
        {
            if (!_isReady || _targetChannel == null)
            {
                _logger.LogWarning("Discord bot is not ready or target channel not found");
                return false;
            }

            try
            {
                await _targetChannel.SendMessageAsync(message);
                _logger.LogInformation("Successfully sent message to Discord");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to Discord");
                return false;
            }
        }

        public async Task<bool> SendMessageToChannelAsync(IMessageChannel channel, string message)
        {
            if (!_isReady)
            {
                _logger.LogWarning("Discord bot is not ready");
                return false;
            }

            try
            {
                await channel.SendMessageAsync(message);
                _logger.LogInformation("Successfully sent response message to user mention");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send response message");
                return false;
            }
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            // Don't respond to system messages, bots, or webhooks
            if (message.Author.IsBot || message.Author.IsWebhook || message is not SocketUserMessage userMessage)
                return;

            // Check if automated responses are enabled
            if (!_config.AutomatedResponse.Enabled)
                return;

            try
            {
                // ONLY respond if the bot is mentioned - this is the key requirement
                if (message.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id))
                {
                    _logger.LogInformation("Bot mentioned by {Username} in {ChannelName}: {MessageContent}",
                        message.Author.Username,
                        (message.Channel as ITextChannel)?.Name ?? "DM",
                        message.Content);

                    await HandleMentionAsync(message);
                }
                // If not mentioned, do nothing - bot stays silent
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message: {MessageContent}", message.Content);
            }
        }

        private async Task HandleMentionAsync(SocketMessage message)
        {
            // Extract command from message (remove mentions and extra whitespace)
            var cleanContent = message.Content;

            // Remove bot mentions from the message
            foreach (var mention in message.MentionedUsers.Where(u => u.Id == _client.CurrentUser.Id))
            {
                cleanContent = cleanContent.Replace($"<@{mention.Id}>", "").Replace($"<@!{mention.Id}>", "");
            }

            cleanContent = cleanContent.Trim().ToLowerInvariant();

            // Determine response based on command
            string response;

            if (string.IsNullOrEmpty(cleanContent))
            {
                // Just mentioned with no command - show default message
                response = _config.AutomatedResponse.DefaultMessage;
            }
            else
            {
                // Extract the command and only respond to configured commands
                var command = ExtractConfiguredCommand(cleanContent);

                if (command == "status")
                {
                    // Use the always online status
                    response = GetAlwaysOnlineStatus();
                }
                else if (!string.IsNullOrEmpty(command) && _config.AutomatedResponse.CommandResponses.TryGetValue(command, out var commandResponse))
                {
                    // Use the configured response for this command
                    response = commandResponse;
                }
                else
                {
                    // Unknown command - show help message
                    response = $"❓ Unknown command. Try one of these:\n\n{_config.AutomatedResponse.CommandResponses.GetValueOrDefault("help", _config.AutomatedResponse.DefaultMessage)}";
                }
            }

            // Send the response to the same channel where the bot was mentioned
            await SendMessageToChannelAsync(message.Channel, response);
        }

        private string? ExtractConfiguredCommand(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            // Get all configured commands from the settings
            var configuredCommands = _config.AutomatedResponse.CommandResponses.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Also add "status" since it has special handling
            configuredCommands.Add("status");

            // Split the content into words
            var words = content.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(w => !string.IsNullOrWhiteSpace(w))
                              .ToList();

            // Look for the first word that matches a configured command
            foreach (var word in words)
            {
                if (configuredCommands.Contains(word))
                {
                    return word.ToLowerInvariant();
                }
            }

            // No configured command found
            return null;
        }

        private string GetAlwaysOnlineStatus()
        {
            var statusMessage = "🟢 **The Thatch Hut** - Online and Ready!\n\n" +
                              "🌍 **World**: Midgard\n" +
                              "👥 **Status**: Accepting brave Vikings!\n" +
                              "🔒 **Password**: Golum2003\n" +
                              "⚔️ **Mods**: ValheimPlus enabled\n" +
                              "📡 **Connection**: Stable and reliable\n" +
                              $"🕒 **Last Checked**: <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>\n\n" +
                              "Ready for your next adventure! 🏔️";

            return statusMessage;
        }

        private async Task<bool> InitializeChannelAsync()
        {
            try
            {
                // First try to get channel by ID if provided
                if (_config.ChannelId.HasValue)
                {
                    _targetChannel = _client.GetChannel(_config.ChannelId.Value) as IMessageChannel;
                    if (_targetChannel != null)
                    {
                        _logger.LogInformation("Found target channel by ID: {ChannelName}", _targetChannel.Name);
                        return true;
                    }
                }

                // If no specific channel ID, search by name in the guild
                if (_config.GuildId.HasValue)
                {
                    var guild = _client.GetGuild(_config.GuildId.Value);
                    if (guild != null)
                    {
                        _targetChannel = guild.TextChannels.FirstOrDefault(c =>
                            c.Name.Equals(_config.ChannelName, StringComparison.OrdinalIgnoreCase));

                        if (_targetChannel != null)
                        {
                            _logger.LogInformation("Found target channel by name: {ChannelName} in guild: {GuildName}",
                                _targetChannel.Name, guild.Name);
                            return true;
                        }
                    }
                }

                // If still not found, search across all guilds
                foreach (var guild in _client.Guilds)
                {
                    _targetChannel = guild.TextChannels.FirstOrDefault(c =>
                        c.Name.Equals(_config.ChannelName, StringComparison.OrdinalIgnoreCase));

                    if (_targetChannel != null)
                    {
                        _logger.LogInformation("Found target channel: {ChannelName} in guild: {GuildName}",
                            _targetChannel.Name, guild.Name);
                        return true;
                    }
                }

                _logger.LogError("Could not find target channel: {ChannelName}", _config.ChannelName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Discord channel");
                return false;
            }
        }

        private Task LogAsync(LogMessage log)
        {
            var logLevel = log.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Debug,
                LogSeverity.Debug => LogLevel.Trace,
                _ => LogLevel.Information
            };

            _logger.Log(logLevel, log.Exception, "[Discord] {Source}: {Message}", log.Source, log.Message);
            return Task.CompletedTask;
        }

        private Task ReadyAsync()
        {
            _logger.LogInformation("Discord bot is ready! Logged in as: {Username}#{Discriminator}",
                _client.CurrentUser.Username, _client.CurrentUser.Discriminator);
            _isReady = true;
            return Task.CompletedTask;
        }

        private Task DisconnectedAsync(Exception exception)
        {
            _logger.LogWarning(exception, "Discord bot disconnected");
            _isReady = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
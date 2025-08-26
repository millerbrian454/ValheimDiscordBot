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
        private readonly ValheimServerService _valheimServerService;
        private IMessageChannel? _targetChannel;
        private bool _isReady = false;

        public DiscordBotService(ILogger<DiscordBotService> logger, DiscordConfiguration config, ValheimServerService valheimServerService)
        {
            _logger = logger;
            _config = config;
            _valheimServerService = valheimServerService;

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
            if (message.Author.IsBot || message.Author.IsWebhook || message is not SocketUserMessage userMessage)
                return;

            if (!_config.AutomatedResponse.Enabled)
                return;

            try
            {
                if (message.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id))
                {
                    _logger.LogInformation("Bot mentioned by {Username} in {ChannelName}: {MessageContent}",
                        message.Author.Username,
                        (message.Channel as ITextChannel)?.Name ?? "DM",
                        message.Content);

                    await HandleMentionAsync(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message: {MessageContent}", message.Content);
            }
        }

        private async Task HandleMentionAsync(SocketMessage message)
        {
            var cleanContent = message.Content;

            foreach (var mention in message.MentionedUsers.Where(u => u.Id == _client.CurrentUser.Id))
            {
                cleanContent = cleanContent.Replace($"<@{mention.Id}>", "").Replace($"<@!{mention.Id}>", "");
            }

            cleanContent = cleanContent.Trim().ToLowerInvariant();

            string response;

            if (string.IsNullOrEmpty(cleanContent))
            {
                response = _config.AutomatedResponse.DefaultMessage;
            }
            else
            {
                var command = ExtractConfiguredCommand(cleanContent);

                if (command == "status")
                {
                    response = await GetRealServerStatusAsync();
                }
                else if (!string.IsNullOrEmpty(command) && _config.AutomatedResponse.CommandResponses.TryGetValue(command, out var commandResponse))
                {
                    response = commandResponse;
                }
                else
                {
                    response = $"❓ Unknown command. Try one of these:\n\n{_config.AutomatedResponse.CommandResponses.GetValueOrDefault("help", _config.AutomatedResponse.DefaultMessage)}";
                }
            }

            await SendMessageToChannelAsync(message.Channel, response);
        }

        private string? ExtractConfiguredCommand(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var configuredCommands = _config.AutomatedResponse.CommandResponses.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            configuredCommands.Add("status");

            var words = content.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(w => !string.IsNullOrWhiteSpace(w))
                              .ToList();

            foreach (var word in words)
            {
                if (configuredCommands.Contains(word))
                {
                    return word.ToLowerInvariant();
                }
            }

            return null;
        }

        private async Task<string> GetRealServerStatusAsync()
        {
            try
            {
                _logger.LogInformation("Checking real Valheim server status...");
                var serverStatus = await _valheimServerService.CheckServerStatusAsync();
                return _valheimServerService.FormatServerStatus(serverStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting real server status, falling back to default");
                return _config.AutomatedResponse.CommandResponses.GetValueOrDefault("status", "🔴 **Server Status Unknown** - Unable to check server status at this time.");
            }
        }

        private Task<bool> InitializeChannelAsync()
        {
            try
            {
                if (_config.ChannelId.HasValue)
                {
                    _targetChannel = _client.GetChannel(_config.ChannelId.Value) as IMessageChannel;
                    if (_targetChannel != null)
                    {
                        _logger.LogInformation("Found target channel by ID: {ChannelName}", _targetChannel.Name);
                        return Task.FromResult(true);
                    }
                }

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
                            return Task.FromResult(true);
                        }
                    }
                }

                foreach (var guild in _client.Guilds)
                {
                    _targetChannel = guild.TextChannels.FirstOrDefault(c =>
                        c.Name.Equals(_config.ChannelName, StringComparison.OrdinalIgnoreCase));

                    if (_targetChannel != null)
                    {
                        _logger.LogInformation("Found target channel: {ChannelName} in guild: {GuildName}",
                            _targetChannel.Name, guild.Name);
                        return Task.FromResult(true);
                    }
                }

                _logger.LogError("Could not find target channel: {ChannelName}", _config.ChannelName);
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Discord channel");
                return Task.FromResult(false);
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
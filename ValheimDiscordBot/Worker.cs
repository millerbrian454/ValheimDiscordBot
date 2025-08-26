using ValheimDiscordBot.Configuration;
using ValheimDiscordBot.Services;

namespace ValheimDiscordBot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DiscordBotService _discordBotService;
        private readonly ValheimServerService _valheimServerService;
        private readonly DiscordConfiguration _config;

        public Worker(ILogger<Worker> logger, DiscordBotService discordBotService, ValheimServerService valheimServerService, DiscordConfiguration config)
        {
            _logger = logger;
            _discordBotService = discordBotService;
            _valheimServerService = valheimServerService;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Discord Bot Worker");

            if (!_config.Enabled)
            {
                _logger.LogInformation("Discord bot is disabled in configuration");
                return;
            }

            // Start the Discord bot
            var started = await _discordBotService.StartAsync();
            if (!started)
            {
                _logger.LogError("Failed to start Discord bot. Stopping worker.");
                return;
            }

            _logger.LogInformation("Discord bot started successfully");

            // Check server status and post the startup message
            try
            {
                _logger.LogInformation("Checking Valheim server status for startup message...");
                var serverStatus = await _valheimServerService.CheckServerStatusAsync();

                string startupMessage;
                if (serverStatus.IsOnline)
                {
                    startupMessage = $"**The Thatch Hut Server is Online and Ready!**\n\n" +
                                   "Server is running and accepting connections!\n" +
                                   $"Bot started at <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>\n\n" +
                                   $"{_config.AutomatedResponse.DefaultMessage}";
                }
                else
                {
                    var statusText = serverStatus.ProcessRunning ? "Starting Up" : "Offline";

                    startupMessage = $"**The Thatch Hut Server is {statusText}**\n\n";

                    if (serverStatus.ProcessRunning)
                    {
                        startupMessage += "Server process is running but not yet accepting connections\n" +
                                        "Please wait a moment for the server to fully start\n";
                    }
                    else
                    {
                        startupMessage += "Server process is not currently running\n" +
                                        "Server may need to be started manually\n";
                    }

                    startupMessage += $"Bot started at <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>\n\n" +
                                    $"{_config.AutomatedResponse.DefaultMessage}";
                }

                await _discordBotService.SendMessageAsync(startupMessage);
                _logger.LogInformation("Posted startup message to Discord with server status: {ServerOnline}", serverStatus.IsOnline);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check server status or post startup message");

                // Fallback to a basic startup message if status check fails
                try
                {
                    var fallbackMessage = $"**The Thatch Hut Discord Bot is Online!**\n\n" +
                                         "Bot started successfully but could not check server status\n" +
                                         $"Started at <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>\n\n" +
                                         $"{_config.AutomatedResponse.DefaultMessage}";

                    await _discordBotService.SendMessageAsync(fallbackMessage);
                    _logger.LogInformation("Posted fallback startup message to Discord");
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Failed to post fallback startup message");
                }
            }

            try
            {
                // Just keep the service running to listen for commands
                // No periodic posting - the bot will only respond to mentions
                _logger.LogInformation("Discord bot is now running and listening for commands...");

                // Wait indefinitely until cancellation is requested
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Discord bot worker shutdown requested");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Discord Bot Worker");
            }
            finally
            {
                await _discordBotService.StopAsync();
                _logger.LogInformation("Discord Bot Worker stopped");
            }
        }
    }
}
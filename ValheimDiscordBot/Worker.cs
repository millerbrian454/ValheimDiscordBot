using ValheimDiscordBot.Configuration;
using ValheimDiscordBot.Services;

namespace ValheimDiscordBot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DiscordBotService _discordBotService;
        private readonly DiscordConfiguration _config;

        public Worker(ILogger<Worker> logger, DiscordBotService discordBotService, DiscordConfiguration config)
        {
            _logger = logger;
            _discordBotService = discordBotService;
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

            // Post the startup message once
            try
            {
                var startupMessage = $" **The Thatch Hut Server is Online!**\n\n{_config.AutomatedResponse.DefaultMessage}";
                await _discordBotService.SendMessageAsync(startupMessage);
                _logger.LogInformation("Posted startup message to Discord");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post startup message");
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
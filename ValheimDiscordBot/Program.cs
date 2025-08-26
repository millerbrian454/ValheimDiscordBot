using ValheimDiscordBot;
using ValheimDiscordBot.Configuration;
using ValheimDiscordBot.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure Discord settings
var discordConfig = new DiscordConfiguration();
builder.Configuration.GetSection(DiscordConfiguration.SectionName).Bind(discordConfig);
builder.Services.AddSingleton(discordConfig);

// Register Valheim server service
builder.Services.AddSingleton<ValheimServerService>();

// Register Discord bot service
builder.Services.AddSingleton<DiscordBotService>();

// Register the worker service
builder.Services.AddHostedService<Worker>();

// Add Windows Service support
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ValheimDiscordBot";
});

var host = builder.Build();
host.Run();
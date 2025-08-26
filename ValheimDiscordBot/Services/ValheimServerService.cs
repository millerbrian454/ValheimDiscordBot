using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using ValheimDiscordBot.Contracts;

namespace ValheimDiscordBot.Services
{
    public class ValheimServerService
    {
        private readonly ILogger<ValheimServerService> _logger;
        private readonly string _processName;
        private readonly int _serverPort;

        public ValheimServerService(ILogger<ValheimServerService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _processName = configuration["ValheimServer:ProcessName"] ?? "valheim_server";
            _serverPort = configuration.GetValue<int>("ValheimServer:Port", 2456);
        }

        public async Task<ServerStatus> CheckServerStatusAsync()
        {
            try
            {
                _logger.LogDebug("Checking Valheim dedicated server status for process: {ProcessName}", _processName);

                // Check if the Valheim server process is running
                var processes = Process.GetProcessesByName(_processName);

                if (processes.Length == 0)
                {
                    _logger.LogInformation("Valheim server process '{ProcessName}' is not running", _processName);
                    return new ServerStatus
                    {
                        IsOnline = false,
                        ProcessRunning = false,
                        PortOpen = false,
                        ResponseTime = null,
                        LastChecked = DateTime.UtcNow,
                        Message = "Valheim server process is not running"
                    };
                }

                // Get process information
                var process = processes[0]; // Take the first one if multiple exist
                var processInfo = new ProcessInfo
                {
                    ProcessId = process.Id,
                    StartTime = process.StartTime,
                    WorkingSet = process.WorkingSet64,
                    ProcessName = process.ProcessName
                };

                // Check if the server port is responding
                var portResult = await CheckPortAsync();

                var status = new ServerStatus
                {
                    IsOnline = portResult.IsOpen,
                    ProcessRunning = true,
                    PortOpen = portResult.IsOpen,
                    ResponseTime = portResult.ResponseTime,
                    LastChecked = DateTime.UtcNow,
                    ProcessInfo = processInfo,
                    Message = portResult.IsOpen
                        ? "Valheim server is running and accepting connections"
                        : "Valheim server process is running but port is not responding"
                };

                // Clean up process references
                foreach (var proc in processes)
                {
                    proc.Dispose();
                }

                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Valheim server status");
                return new ServerStatus
                {
                    IsOnline = false,
                    ProcessRunning = false,
                    PortOpen = false,
                    ResponseTime = null,
                    LastChecked = DateTime.UtcNow,
                    Message = $"Error checking server: {ex.Message}"
                };
            }
        }

        private async Task<PortCheckResult> CheckPortAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("Checking UDP port {Port} for Valheim server", _serverPort);

                // First try UDP check (Valheim's primary protocol)
                var udpResult = await CheckUdpPortAsync();
                if (udpResult.IsOpen)
                {
                    stopwatch.Stop();
                    _logger.LogDebug("UDP port check successful for port {Port}", _serverPort);
                    return new PortCheckResult
                    {
                        IsOpen = true,
                        ResponseTime = (int)stopwatch.ElapsedMilliseconds
                    };
                }

                // Fallback to TCP check (some servers might also listen on TCP)
                _logger.LogDebug("UDP check failed, trying TCP check for port {Port}", _serverPort);
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync("127.0.0.1", _serverPort);
                var timeoutTask = Task.Delay(2000); // 2 second timeout for local connection

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                stopwatch.Stop();

                if (completedTask == connectTask && tcpClient.Connected)
                {
                    _logger.LogDebug("TCP port check successful for port {Port}", _serverPort);
                    return new PortCheckResult
                    {
                        IsOpen = true,
                        ResponseTime = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                else
                {
                    _logger.LogDebug("Both UDP and TCP port checks failed for port {Port}", _serverPort);
                    return new PortCheckResult
                    {
                        IsOpen = false,
                        ResponseTime = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogDebug(ex, "Port check failed for localhost:{Port}", _serverPort);
                return new PortCheckResult
                {
                    IsOpen = false,
                    ResponseTime = (int)stopwatch.ElapsedMilliseconds
                };
            }
        }

        private async Task<PortCheckResult> CheckUdpPortAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var udpClient = new UdpClient();
                udpClient.Client.ReceiveTimeout = 1000; // 1 second timeout
                udpClient.Client.SendTimeout = 1000;

                // Connect to the server endpoint
                var serverEndpoint = new IPEndPoint(IPAddress.Loopback, _serverPort);

                // Send a basic UDP packet to test connectivity
                // Valheim uses a specific protocol, but we're just testing if the port is open
                var testData = new byte[] { 0x00, 0x00, 0x00, 0x00 }; // Simple test packet

                await udpClient.SendAsync(testData, testData.Length, serverEndpoint);

                // Try to receive a response (or timeout)
                try
                {
                    var receiveTask = udpClient.ReceiveAsync();
                    var timeoutTask = Task.Delay(1000); // 1 second timeout

                    var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                    stopwatch.Stop();

                    if (completedTask == receiveTask)
                    {
                        // We got some response, port is likely open
                        return new PortCheckResult
                        {
                            IsOpen = true,
                            ResponseTime = (int)stopwatch.ElapsedMilliseconds
                        };
                    }
                    else
                    {
                        // Timeout - but for UDP, this doesn't necessarily mean the port is closed
                        // The server might be running but not responding to our test packet
                        // We'll consider this as "possibly open" and let the process check be the primary indicator
                        return new PortCheckResult
                        {
                            IsOpen = false,
                            ResponseTime = (int)stopwatch.ElapsedMilliseconds
                        };
                    }
                }
                catch (SocketException)
                {
                    // Socket error usually means port is not open or not listening
                    stopwatch.Stop();
                    return new PortCheckResult
                    {
                        IsOpen = false,
                        ResponseTime = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogDebug(ex, "UDP port check failed for port {Port}", _serverPort);
                return new PortCheckResult
                {
                    IsOpen = false,
                    ResponseTime = (int)stopwatch.ElapsedMilliseconds
                };
            }
        }

        public string FormatServerStatus(ServerStatus status)
        {
            var statusEmoji = status.IsOnline ? "🟢" : "🔴";
            var statusText = status.IsOnline ? "Online" : "Offline";

            var message = $"{statusEmoji} **The Thatch Hut** - {statusText}\n\n";

            if (status.IsOnline)
            {
                message += "🌍 **World**: Midgard\n" +
                          "👥 **Status**: Accepting brave Vikings!\n" +
                          "🔒 **Password**: Golum2003\n" +
                          "⚔️ **Mods**: ValheimPlus enabled\n" +
                          "📡 **Connection**: Ready for adventure\n";

                if (status.ResponseTime.HasValue)
                {
                    message += $"⚡ **Response Time**: {status.ResponseTime}ms\n";
                }

                if (status.ProcessInfo != null)
                {
                    var uptime = DateTime.Now - status.ProcessInfo.StartTime;
                    var memoryMB = status.ProcessInfo.WorkingSet / (1024 * 1024);
                    message += $"🖥️ **Process**: {status.ProcessInfo.ProcessName} (PID: {status.ProcessInfo.ProcessId})\n";
                    message += $"⏱️ **Uptime**: {FormatUptime(uptime)}\n";
                    message += $"🧠 **Memory**: {memoryMB:N0} MB\n";
                }
            }
            else
            {
                message += $"❌ **Issue**: {status.Message}\n";

                if (status.ProcessRunning)
                {
                    message += "🔄 **Process Status**: Running (but port not responding)\n";
                    if (status.ProcessInfo != null)
                    {
                        var uptime = DateTime.Now - status.ProcessInfo.StartTime;
                        message += $"⏱️ **Process Uptime**: {FormatUptime(uptime)}\n";
                    }
                    message += "🔧 **Suggestion**: Server may be starting up or experiencing issues\n";
                }
                else
                {
                    message += "🔄 **Process Status**: Not Running\n";
                    message += "🔧 **Suggestion**: Start the Valheim dedicated server\n";
                }
            }

            message += $"🕒 **Last Checked**: <t:{new DateTimeOffset(status.LastChecked).ToUnixTimeSeconds()}:R>\n\n";

            if (status.IsOnline)
            {
                message += "Ready for your next adventure! 🏔️";
            }
            else
            {
                message += "Please wait for server to start or check server status. 🏔️";
            }

            return message;
        }

        private string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1)
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
            else if (uptime.TotalHours >= 1)
                return $"{uptime.Hours}h {uptime.Minutes}m";
            else
                return $"{uptime.Minutes}m {uptime.Seconds}s";
        }
    }
}
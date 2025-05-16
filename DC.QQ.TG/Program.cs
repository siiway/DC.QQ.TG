using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DC.QQ.TG.Adapters;
using DC.QQ.TG.Interfaces;
using DC.QQ.TG.Services;
using DC.QQ.TG.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
using Spectre.Console;

namespace DC.QQ.TG
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Define command line arguments
            var switchMappings = new Dictionary<string, string>()
            {
                // NapCat (QQ) parameters
                { "--napcat-url", "NapCat:BaseUrl" },
                { "--napcat-token", "NapCat:Token" },
                { "--qq-group", "NapCat:GroupId" },

                // Discord parameters
                { "--discord-webhook", "Discord:WebhookUrl" },
                { "--discord-webhook-url", "Discord:WebhookUrl" },
                { "--discord-bot-token", "Discord:BotToken" },
                { "--discord-guild-id", "Discord:GuildId" },
                { "--discord-channel-id", "Discord:ChannelId" },
                { "--discord-use-proxy", "Discord:UseProxy" },
                { "--auto-webhook", "Discord:AutoWebhook" },
                { "--discord-webhook-name", "Discord:WebhookName" },

                // Telegram parameters
                { "--telegram-token", "Telegram:BotToken" },
                { "--telegram-bot-token", "Telegram:BotToken" },
                { "--telegram-chat", "Telegram:ChatId" },
                { "--telegram-chat-id", "Telegram:ChatId" },
                { "--telegram-webhook-url", "Telegram:WebhookUrl" },
                { "--telegram-webhook-port", "Telegram:WebhookPort" },
                { "--telegram-certificate-path", "Telegram:CertificatePath" },
                { "--telegram-certificate-password", "Telegram:CertificatePassword" },

                // Platform control
                { "--disable-telegram", "Disabled:Telegram" },
                { "--disable-discord", "Disabled:Discord" },
                { "--disable-qq", "Disabled:QQ" },

                // Debug parameters
                { "--show-napcat-response", "Debug:ShowNapCatResponse" },
                { "--debug-shell", "Debug:EnableShell" }
            };

            // Create configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args, switchMappings)
                .Build();

            // Validate required configuration parameters exist
            ValidateConfiguration(configuration);

            // Create a service provider for validation
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IConfiguration>(configuration);
            serviceCollection.AddLogging(builder =>
            {
                builder.AddSpectreConsoleLogger();
                builder.AddDebug();

                // Set minimum log level
                builder.SetMinimumLevel(LogLevel.Information);

                // Configure specific categories
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddFilter("DC.QQ.TG", LogLevel.Debug);
            });
            serviceCollection.AddHttpClient();
            serviceCollection.AddTransient<ValidationService>();
            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Validate API keys and connections
            var validationService = serviceProvider.GetRequiredService<ValidationService>();

            AnsiConsole.Status()
                .Start("[yellow]Validating connections...[/]", ctx =>
                {
                    // Add a spinner animation
                    ctx.Spinner(Spinner.Known.Dots);
                    ctx.SpinnerStyle(Style.Parse("yellow"));

                    // Run the validation
                    var validationTask = validationService.ValidateAllServicesAsync();

                    // Wait for the validation to complete
                    validationTask.Wait();

                    // Check the result
                    if (!validationTask.Result)
                    {
                        AnsiConsole.MarkupLine("");
                        var failPanel = new Panel(
                            new Markup("[white]Please check your API keys and connections.[/]"))
                            .Header("[red]Validation Failed[/]")
                            .Border(BoxBorder.Double)
                            .BorderColor(Color.Red)
                            .Expand();

                        AnsiConsole.Write(failPanel);
                        Environment.Exit(1);
                    }

                    AnsiConsole.MarkupLine("");
                });

            // Show success message
            var successPanel = new Panel(
                new Markup("[white]All enabled services validated successfully![/]"))
                .Header("[green]Validation Successful[/]")
                .Border(BoxBorder.Double)
                .BorderColor(Color.Green)
                .Expand();

            AnsiConsole.Write(successPanel);

            var host = CreateHostBuilder(args, switchMappings).Build();

            // 注册应用程序退出事件，清理临时文件
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger<Program>();

                logger.LogInformation("Application shutting down, cleaning up temporary files...");
                FileDownloader.CleanupAllFiles(logger);
                logger.LogInformation("Temporary files cleanup completed");
            };

            await host.RunAsync();
        }

        private static void ValidateConfiguration(IConfiguration configuration)
        {
            var missingParams = new List<string>();
            bool discordDisabled = configuration["Disabled:Discord"]?.ToLower() == "true";
            bool telegramDisabled = configuration["Disabled:Telegram"]?.ToLower() == "true";
            bool qqDisabled = configuration["Disabled:QQ"]?.ToLower() == "true";

            // Check Discord configuration if not disabled
            if (!discordDisabled)
            {
                // Check if either webhook URL or bot token is provided
                bool hasWebhook = !string.IsNullOrEmpty(configuration["Discord:WebhookUrl"]);
                bool hasBotToken = !string.IsNullOrEmpty(configuration["Discord:BotToken"]);

                if (!hasWebhook && !hasBotToken)
                {
                    missingParams.Add("--discord-webhook-url or --discord-bot-token");
                }

                // If bot token is provided, check for guild ID and channel ID
                if (hasBotToken)
                {
                    if (string.IsNullOrEmpty(configuration["Discord:GuildId"]))
                    {
                        missingParams.Add("--discord-guild-id");
                    }

                    if (string.IsNullOrEmpty(configuration["Discord:ChannelId"]))
                    {
                        missingParams.Add("--discord-channel-id");
                    }
                }
            }

            // Check Telegram configuration if not disabled
            if (!telegramDisabled)
            {
                if (string.IsNullOrEmpty(configuration["Telegram:BotToken"]))
                {
                    missingParams.Add("--telegram-token");
                }

                if (string.IsNullOrEmpty(configuration["Telegram:ChatId"]))
                {
                    missingParams.Add("--telegram-chat");
                }
            }

            // Check NapCat configuration if not disabled
            if (!qqDisabled)
            {
                if (string.IsNullOrEmpty(configuration["NapCat:BaseUrl"]))
                {
                    missingParams.Add("--napcat-url");
                }

                if (string.IsNullOrEmpty(configuration["NapCat:Token"]))
                {
                    missingParams.Add("--napcat-token");
                }

                if (string.IsNullOrEmpty(configuration["NapCat:GroupId"]))
                {
                    missingParams.Add("--qq-group");
                }
            }

            // Check if debug shell is enabled
            bool debugShellEnabled = configuration["Debug:EnableShell"]?.ToLower() == "true";

            // Check if at least one platform is enabled (unless debug shell is enabled)
            if ((!debugShellEnabled && discordDisabled && telegramDisabled && qqDisabled) ||
                (missingParams.Count > 0 &&
                 ((!discordDisabled && missingParams.Contains("--discord-webhook")) ||
                  (!telegramDisabled && (missingParams.Contains("--telegram-token") || missingParams.Contains("--telegram-chat"))) ||
                  (!qqDisabled && (missingParams.Contains("--napcat-url") || missingParams.Contains("--napcat-token"))))))
            {
                // Show configuration error
                var errorPanel = new Panel(Align.Center(
                    discordDisabled && telegramDisabled && qqDisabled
                        ? new Markup("[bold red]At least one platform must be enabled[/]")
                        : new Rows(
                            new Markup("[bold]Missing required parameters:[/]"),
                            new Markup(""),
                            new Rows(missingParams.Select(p => new Markup($"[red]ERROR:[/] {p}")))
                        )
                ))
                .Header("[red]Configuration Error[/]")
                .Border(BoxBorder.Double)
                .BorderColor(Color.Red)
                .Expand();

                AnsiConsole.Write(errorPanel);

                // Show usage examples
                var examplesPanel = new Panel(
                    new Rows(
                        new Markup("[bold]Full configuration with webhook:[/]"),
                        new Markup(""),
                        new Markup("dotnet run -- --discord-webhook-url=<url> --telegram-bot-token=<token> --telegram-chat-id=<chat_id> --napcat-url=<url> --napcat-token=<token> --qq-group=<group_id>"),
                        new Markup(""),
                        new Markup("[bold]Full configuration with Discord bot:[/]"),
                        new Markup(""),
                        new Markup("dotnet run -- --discord-bot-token=<token> --discord-guild-id=<guild_id> --discord-channel-id=<channel_id> --telegram-bot-token=<token> --telegram-chat-id=<chat_id> --napcat-url=<url> --napcat-token=<token> --qq-group=<group_id>"),
                        new Markup(""),
                        new Markup("[bold]Disable specific platforms:[/]"),
                        new Markup(""),
                        new Markup("dotnet run -- --disable-telegram=true --discord-webhook-url=<url> --napcat-url=<url> --napcat-token=<token> --qq-group=<group_id>"),
                        new Markup("dotnet run -- --disable-discord=true --telegram-bot-token=<token> --telegram-chat-id=<chat_id> --napcat-url=<url> --napcat-token=<token> --qq-group=<group_id>"),
                        new Markup("dotnet run -- --disable-qq=true --discord-webhook-url=<url> --telegram-bot-token=<token> --telegram-chat-id=<chat_id>")
                    )
                )
                .Header("[yellow]Usage Examples[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Yellow)
                .Expand();

                AnsiConsole.Write(examplesPanel);
                Environment.Exit(1);
            }

            // Log which platforms are enabled/disabled using Spectre.Console
            AnsiConsole.Write(new Rule("[cyan]Platform Status[/]").RuleStyle("grey").DoubleBorder());

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("Platform").Centered())
                .AddColumn(new TableColumn("Status").Centered())
                .AddColumn(new TableColumn("Details").LeftAligned());

            // Determine Discord details
            string discordDetails = "[dim]Discord messaging is disabled[/]";
            if (!discordDisabled)
            {
                bool hasWebhook = !string.IsNullOrEmpty(configuration["Discord:WebhookUrl"]);
                bool hasBotToken = !string.IsNullOrEmpty(configuration["Discord:BotToken"]);

                if (hasWebhook && hasBotToken)
                {
                    discordDetails = "Discord webhook and bot will be used for messaging";
                }
                else if (hasWebhook)
                {
                    discordDetails = "Discord webhook will be used for messaging (one-way)";
                }
                else if (hasBotToken)
                {
                    discordDetails = "Discord bot will be used for messaging";
                }
            }

            table.AddRow(
                new Markup("[bold]Discord[/]"),
                discordDisabled
                    ? new Markup("[dim]DISABLED[/]")
                    : new Markup("[green]ENABLED[/]"),
                new Markup(discordDetails)
            );

            table.AddRow(
                new Markup("[bold]Telegram[/]"),
                telegramDisabled
                    ? new Markup("[dim]DISABLED[/]")
                    : new Markup("[green]ENABLED[/]"),
                telegramDisabled
                    ? new Markup("[dim]Telegram messaging is disabled[/]")
                    : new Markup("Telegram bot will be used for messaging")
            );

            table.AddRow(
                new Markup("[bold]QQ[/]"),
                qqDisabled
                    ? new Markup("[dim]DISABLED[/]")
                    : new Markup("[green]ENABLED[/]"),
                qqDisabled
                    ? new Markup("[dim]QQ messaging is disabled[/]")
                    : new Markup($"QQ group {configuration["NapCat:GroupId"]} will be used for messaging")
            );

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        public static IHostBuilder CreateHostBuilder(string[] args, Dictionary<string, string> switchMappings) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args, switchMappings);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var configuration = hostContext.Configuration;
                    bool discordDisabled = configuration["Disabled:Discord"]?.ToLower() == "true";
                    bool telegramDisabled = configuration["Disabled:Telegram"]?.ToLower() == "true";
                    bool qqDisabled = configuration["Disabled:QQ"]?.ToLower() == "true";

                    // Register HTTP clients
                    services.AddHttpClient();

                    // Register adapters based on disabled settings
                    if (!qqDisabled)
                    {
                        services.AddSingleton<IMessageAdapter, QQAdapter>();
                    }

                    if (!telegramDisabled)
                    {
                        services.AddSingleton<IMessageAdapter, TelegramAdapter>();
                    }

                    // idk but discord init will stop init process
                    // that's why we put it last
                    // hoping we can fix it later
                    if (!discordDisabled)
                    {
                        services.AddSingleton<IMessageAdapter, DiscordAdapter>();
                    }

                    // Register services
                    services.AddSingleton<MessageService>();
                    services.AddHostedService(provider => provider.GetRequiredService<MessageService>());

                    // Register debug shell service if enabled
                    if (hostContext.Configuration["Debug:EnableShell"]?.ToLower() == "true")
                    {
                        services.AddHostedService<DebugShellService>();
                    }
                })
                .ConfigureLogging((hostContext, logging) =>
                {
                    logging.ClearProviders();

                    // Check if debug shell is enabled
                    bool debugShellEnabled = hostContext.Configuration["Debug:EnableShell"]?.ToLower() == "true";

                    if (debugShellEnabled)
                    {
                        // Add DebugShellLogger if debug shell is enabled
                        logging.AddDebugShellLogger(() => true);
                    }
                    else
                    {
                        // Add SpectreConsoleLogger if debug shell is disabled
                        logging.AddSpectreConsoleLogger();
                    }

                    logging.AddDebug();

                    // Set minimum log level
                    logging.SetMinimumLevel(LogLevel.Information);

                    // Configure specific categories
                    logging.AddFilter("Microsoft", LogLevel.Warning);
                    logging.AddFilter("System", LogLevel.Warning);
                    logging.AddFilter("DC.QQ.TG", LogLevel.Debug);
                });
    }
}

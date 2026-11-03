using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using IdlingLightManager.Models;
using IdlingLightManager.Services;
using IdlingLightManager.UI;
using IdlingLightManager.UI.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace IdlingLightManager;

/// <summary>
/// Application entry point.
/// </summary>
[SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "Entry point necessarily couples many types.")]
internal static partial class Program
{
    /// <summary>
    /// Application entry point. Integrates the Generic Host with the WinForms message loop.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Held as a singleton; the target ListView is attached later (see MainForm).
        ListViewSink listViewSink = new();

        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                // Local overlay (git-ignored) that overrides the placeholders in appsettings.json.
                config.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
            })
            .UseSerilog((ctx, cfg) =>
                cfg.ReadFrom.Configuration(ctx.Configuration)
                   .Enrich.FromLogContext()
                   .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                   .WriteTo.Sink(listViewSink))
            .ConfigureServices((ctx, services) =>
            {
                services.AddOptions<IdleDetectionOptions>()
                    .Bind(ctx.Configuration.GetSection("IdleDetection"))
                    .ValidateOnStart();
                services.AddOptions<LightControlOptions>()
                    .Bind(ctx.Configuration.GetSection("LightControl"))
                    .Validate(
                        o => !string.IsNullOrEmpty(o.DeviceId) && !string.IsNullOrEmpty(o.ApiToken),
                        "LightControl の設定が不完全です: DeviceId と ApiToken の設定が必要です。")
                    .ValidateOnStart();

                // Inject HttpClient via IHttpClientFactory for socket reuse and DNS refresh.
                services.AddHttpClient<LightControlService>((sp, client) =>
                {
                    LightControlOptions opts = sp.GetRequiredService<IOptions<LightControlOptions>>().Value;
                    client.BaseAddress = opts.ApiBaseUrl;
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", opts.ApiToken);
                })
                .AddStandardResilienceHandler();

                services.AddHostedService<IdleDetectionService>();

                services.AddSingleton(listViewSink);
                services.AddSingleton<MainForm>();
                services.AddSingleton<TrayApplicationContext>();
            })
            .Build();

        ILoggerFactory loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        ILogger appLogger = loggerFactory.CreateLogger("Program");
        Application.ThreadException += (_, e) =>
            LogThreadException(appLogger, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogDomainException(appLogger, e.ExceptionObject as Exception);

        Task hostTask = host.RunAsync();

        TrayApplicationContext tray = host.Services.GetRequiredService<TrayApplicationContext>();
        Application.Run(tray);

        host.StopAsync().GetAwaiter().GetResult();
        hostTask.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Logs an unhandled thread exception at critical level.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception that occurred.</param>
    [LoggerMessage(Level = LogLevel.Critical, Message = "未処理のスレッド例外が発生しました。")]
    private static partial void LogThreadException(ILogger logger, Exception exception);

    /// <summary>
    /// Logs an unhandled domain exception at critical level.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception that occurred; may be <see langword="null"/>.</param>
    [LoggerMessage(Level = LogLevel.Critical, Message = "未処理のドメイン例外が発生しました。")]
    private static partial void LogDomainException(ILogger logger, Exception? exception);
}

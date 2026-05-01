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
/// アプリケーションのエントリーポイントクラス。
/// </summary>
[SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "Entry point necessarily couples many types.")]
internal static partial class Program
{
    /// <summary>
    /// アプリケーションのエントリーポイント。Generic Host と WinForms メッセージループを統合する。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // ListViewSink はシングルトンとして保持し、後から ListView をセットする
        ListViewSink listViewSink = new();

        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                // appsettings.json のプレースホルダーを実値で上書きするローカル設定
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

                // IHttpClientFactory 経由で HttpClient を注入し、ソケット再利用と DNS 更新に対応する
                services.AddHttpClient<LightControlService>((sp, client) =>
                {
                    LightControlOptions opts = sp.GetRequiredService<IOptions<LightControlOptions>>().Value;
                    client.BaseAddress = opts.ApiBaseUrl;
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", opts.ApiToken);
                })
                .AddStandardResilienceHandler();

                services.AddHostedService<IdleDetectionService>();

                // ListViewSink をシングルトンとして DI コンテナに登録する
                services.AddSingleton(listViewSink);
                services.AddSingleton<MainForm>();
                services.AddSingleton<TrayApplicationContext>();
            })
            .Build();

        // 未処理例外をログに記録する
        ILoggerFactory loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        ILogger appLogger = loggerFactory.CreateLogger("Program");
        Application.ThreadException += (_, e) =>
            LogThreadException(appLogger, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogDomainException(appLogger, e.ExceptionObject as Exception);

        // Generic Host をバックグラウンドで起動する
        Task hostTask = host.RunAsync();

        TrayApplicationContext tray = host.Services.GetRequiredService<TrayApplicationContext>();
        Application.Run(tray);

        // WinForms が終了したらホストも停止する
        host.StopAsync().GetAwaiter().GetResult();
        hostTask.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 未処理のスレッド例外をクリティカルレベルでログに記録する。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">発生した例外。</param>
    [LoggerMessage(Level = LogLevel.Critical, Message = "未処理のスレッド例外が発生しました。")]
    private static partial void LogThreadException(ILogger logger, Exception exception);

    /// <summary>
    /// 未処理のドメイン例外をクリティカルレベルでログに記録する。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">発生した例外。null の場合もある。</param>
    [LoggerMessage(Level = LogLevel.Critical, Message = "未処理のドメイン例外が発生しました。")]
    private static partial void LogDomainException(ILogger logger, Exception? exception);
}

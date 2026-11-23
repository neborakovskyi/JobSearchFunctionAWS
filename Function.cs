using Amazon.Lambda.CloudWatchEvents.ScheduledEvents;
using Amazon.Lambda.Core;
using JobSearchFunction.Models;
using JobSearchFunction.Scrapers;
using JobSearchFunction.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobSearchFunction;

/// <summary>
/// Entry point invoked by EventBridge Scheduler daily at 09:30 Kyiv time.
///
/// Lambda reuses the same instance across warm invocations, so:
///   - DI container is built once in the constructor (via Program.cs).
///   - A new IServiceScope is created per invocation to honour Scoped lifetimes.
///
/// Configuration is read exclusively from environment variables —
/// set them in the Lambda console or via SAM/template.yaml.
/// </summary>
public sealed class Function
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Function>    _logger;

    public Function(IServiceProvider provider)
    {
        _scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        _logger       = provider.GetRequiredService<ILogger<Function>>();
    }

    // ── Handler ───────────────────────────────────────────────────────────────

    public async Task FunctionHandler(ScheduledEvent _, ILambdaContext context)
    {
        // New scope per invocation keeps Scoped services isolated
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var scrapers     = sp.GetServices<IJobScraper>().ToList();
        var analyzer     = sp.GetRequiredService<JobAnalyzerService>();
        var emailService = sp.GetRequiredService<EmailService>();

        // Reserve 15 s for email sending before Lambda hard-kills the process
        var deadline = Math.Max(15, context.RemainingTime.TotalSeconds - 15);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(deadline));

        _logger.LogInformation(
            "Job search pipeline started. Lambda remaining time: {Remaining:F0}s",
            context.RemainingTime.TotalSeconds);

        // ── 1. Scrape all sources in parallel ─────────────────────────────
        var tasks   = scrapers.Select(s => ScrapeWithFallback(s, cts.Token));
        var batches = await Task.WhenAll(tasks);
        var all     = batches.SelectMany(b => b).ToList();

        _logger.LogInformation("Total raw vacancies scraped: {Count}", all.Count);

        // ── 2. Score + rank ───────────────────────────────────────────────
        var top     = analyzer.AnalyzeAndRank(all, topN: 10);
        var summary = analyzer.GenerateSummary(top);

        _logger.LogInformation(
            "After scoring: {Count} selected | avg score: {Avg:F0}",
            top.Count,
            top.Count > 0 ? top.Average(v => v.RelevanceScore) : 0);

        // ── 3. Send email ─────────────────────────────────────────────────
        if (top.Count > 0)
            await emailService.SendReportAsync(top, summary, cts.Token);
        else
            _logger.LogWarning("No relevant vacancies today — email skipped");

        _logger.LogInformation("Pipeline completed successfully");
    }

    // ── DI composition root ───────────────────────────────────────────────────
    // Static so Program.cs can call it before constructing Function.

    public static IServiceProvider BuildServiceProvider()
    {
        // In Lambda all config comes from environment variables.
        // Local testing: set them in launchSettings.json or shell.
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(config);

        services.AddLogging(b => b
            .AddConsole()
            .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information));

        services.AddHttpClient("JobSearch", client =>
        {
            client.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add(
                "Accept-Language", "uk-UA,uk;q=0.9,en;q=0.7");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Scrapers — Scoped: new instance per Lambda invocation scope
        services.AddScoped<IJobScraper, DjinniScraper>();
        services.AddScoped<IJobScraper, DouScraper>();
        services.AddScoped<IJobScraper, WorkUaScraper>();
        services.AddScoped<IJobScraper, RabotaUaScraper>();

        services.AddScoped<JobAnalyzerService>();
        services.AddScoped<EmailService>();

        return services.BuildServiceProvider();
    }

    // ── Fault isolation ───────────────────────────────────────────────────────

    private async Task<IReadOnlyList<JobVacancy>> ScrapeWithFallback(
        IJobScraper scraper, CancellationToken ct)
    {
        try
        {
            return await scraper.ScrapeAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Scraper [{Source}] failed — continuing with other sources",
                scraper.SourceName);
            return [];
        }
    }
}

using HtmlAgilityPack;
using JobSearchFunction.Models;
using Microsoft.Extensions.Logging;

namespace JobSearchFunction.Scrapers;

/// <summary>
/// Scrapes jobs.dou.ua.
/// DOU renders the first ~20 jobs server-side; additional pages require XHR.
/// Two categories are queried: .NET and Fullstack.
/// NOTE: DOU may require a valid session cookie for full results.
///       If the scraper returns 0 results, enable cookie handling or
///       switch to the XHR endpoint (POST /vacancies/xhr-load/?lang=uk).
/// </summary>
public sealed class DouScraper(
    IHttpClientFactory httpClientFactory,
    ILogger<DouScraper> logger) : ScraperBase, IJobScraper
{
    public string SourceName => "DOU";

    private static readonly string[] SearchUrls =
    [
        "https://jobs.dou.ua/vacancies/?category=.NET&remote",
        "https://jobs.dou.ua/vacancies/?category=Fullstack&remote"
    ];

    public async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(CancellationToken ct = default)
    {
        var results = new List<JobVacancy>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var client  = httpClientFactory.CreateClient("JobSearch");

        // DOU checks the Referer header
        client.DefaultRequestHeaders.Referrer = new Uri("https://jobs.dou.ua/");

        foreach (var url in SearchUrls)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(2_000), ct);

            try
            {
                var html = await client.GetStringAsync(url, ct);
                foreach (var v in ParsePage(html).Where(v => seen.Add(v.Url)))
                    results.Add(v);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "[DOU] HTTP error for {Url}", url);
            }
        }

        logger.LogInformation("[DOU] Found {Count} unique vacancies", results.Count);
        return results;
    }

    // ── HTML parsing ──────────────────────────────────────────────────────────
    // DOU structure (stable since 2022):
    //   <ul class="lt"> → <li class="l-vacancy"> → <a class="vt"> (title+url)
    //                                             → <a class="company"> (name)
    //                                             → <div class="sh-info"> (snippet)
    //                                             → <span class="salary"> (optional)

    private IEnumerable<JobVacancy> ParsePage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var items = doc.DocumentNode.SelectNodes(
            "//ul[contains(@class,'lt')]//li[contains(@class,'l-vacancy')]");

        if (items is null) yield break;

        foreach (var item in items)
        {
            var v = TryParseItem(item);
            if (v is not null) yield return v;
        }
    }

    private JobVacancy? TryParseItem(HtmlNode item)
    {
        try
        {
            var titleNode = item.SelectSingleNode(".//a[contains(@class,'vt')]");
            if (titleNode is null) return null;

            var title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
            var url   = titleNode.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrEmpty(url)) return null;

            var company = HtmlEntity.DeEntitize(
                item.SelectSingleNode(".//a[contains(@class,'company')]")
                    ?.InnerText ?? "N/A").Trim();

            var salary = item
                .SelectSingleNode(".//span[contains(@class,'salary')]")
                ?.InnerText.Trim();

            var snippet = HtmlEntity.DeEntitize(
                item.SelectSingleNode(".//div[contains(@class,'sh-info')]")
                    ?.InnerText ?? string.Empty).Trim();

            var fullText = $"{title} {company} {snippet}";

            return new JobVacancy
            {
                Title          = title,
                Company        = company,
                Source         = SourceName,
                Url            = url,
                Salary         = string.IsNullOrWhiteSpace(salary) ? null : salary,
                WorkFormat     = DetectWorkFormat(fullText),
                EmploymentType = DetectEmploymentType(fullText),
                HasReservation = HasReservationMention(fullText),
                MatchedSkills  = ExtractMatchedSkills(fullText),
                SeniorityLevel = DetectSeniority(title),
                Description    = snippet
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[DOU] Failed to parse item");
            return null;
        }
    }
}

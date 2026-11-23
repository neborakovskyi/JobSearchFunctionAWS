using HtmlAgilityPack;
using JobSearchFunction.Models;
using Microsoft.Extensions.Logging;

namespace JobSearchFunction.Scrapers;

/// <summary>
/// Scrapes djinni.co job listings.
/// Selectors target the current HTML structure (2024–2025).
/// If Djinni updates its markup, adjust XPath expressions in ParsePage().
/// </summary>
public sealed class DjinniScraper(
    IHttpClientFactory httpClientFactory,
    ILogger<DjinniScraper> logger) : ScraperBase, IJobScraper
{
    private const string BaseUrl = "https://djinni.co";

    public string SourceName => "Djinni";

    // Three complementary queries to maximise coverage
    private static readonly string[] SearchUrls =
    [
        // Senior/Middle .NET + Angular (last 24 h)
        $"{BaseUrl}/jobs/?primary_keyword=.NET&exp_level=3y&exp_level=5y&date_range=1&keywords=angular",
        // Fullstack .NET + C# (last 24 h)
        $"{BaseUrl}/jobs/?primary_keyword=Fullstack&exp_level=3y&exp_level=5y&date_range=1&keywords=.net+c%23",
        // Middle .NET + TypeScript
        $"{BaseUrl}/jobs/?primary_keyword=.NET&exp_level=2y&date_range=1&keywords=angular+typescript"
    ];

    public async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(CancellationToken ct = default)
    {
        var results = new List<JobVacancy>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var client  = httpClientFactory.CreateClient("JobSearch");

        foreach (var url in SearchUrls)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(1_500), ct); // polite delay

            try
            {
                var html = await client.GetStringAsync(url, ct);
                foreach (var v in ParsePage(html).Where(v => seen.Add(v.Url)))
                    results.Add(v);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "[Djinni] HTTP error for {Url}", url);
            }
        }

        logger.LogInformation("[Djinni] Found {Count} unique vacancies", results.Count);
        return results;
    }

    // ── HTML parsing ──────────────────────────────────────────────────────────

    private IEnumerable<JobVacancy> ParsePage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var items = doc.DocumentNode.SelectNodes(
            "//ul[contains(@class,'list-jobs')]" +
            "//li[contains(@class,'list-jobs__item')]");

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
            // Title + URL
            var titleNode = item.SelectSingleNode(
                ".//a[contains(@class,'job-list-item__link')]");
            if (titleNode is null) return null;

            var title  = Decode(titleNode.InnerText);
            var relUrl = titleNode.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrEmpty(relUrl)) return null;
            var url = relUrl.StartsWith("http") ? relUrl : $"{BaseUrl}{relUrl}";

            // Company
            var company = Decode(item
                .SelectSingleNode(".//a[contains(@class,'job-list-item__company')]")
                ?.InnerText ?? "N/A");

            // Salary (optional)
            var salary = item
                .SelectSingleNode(".//span[contains(@class,'public-salary-item')]")
                ?.InnerText.Trim();

            // Additional info text used for keyword detection
            var info = item
                .SelectSingleNode(".//span[contains(@class,'job-list-item__job-info')]")
                ?.InnerText ?? string.Empty;

            var description = Decode(item
                .SelectSingleNode(".//p[contains(@class,'job-list-item__description')]")
                ?.InnerText ?? string.Empty);

            var fullText = $"{title} {company} {description} {info}";

            return new JobVacancy
            {
                Title          = title,
                Company        = company,
                Source         = SourceName,
                Url            = url,
                Salary         = NullIfEmpty(salary),
                WorkFormat     = DetectWorkFormat(fullText),
                EmploymentType = DetectEmploymentType(fullText),
                HasReservation = HasReservationMention(fullText),
                MatchedSkills  = ExtractMatchedSkills(fullText),
                SeniorityLevel = DetectSeniority(title),
                Description    = description
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Djinni] Failed to parse item");
            return null;
        }
    }

    private static string  Decode(string s)        => HtmlEntity.DeEntitize(s).Trim();
    private static string? NullIfEmpty(string? s)  => string.IsNullOrWhiteSpace(s) ? null : s?.Trim();
}

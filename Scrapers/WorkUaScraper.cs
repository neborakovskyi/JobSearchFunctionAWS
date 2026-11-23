using HtmlAgilityPack;
using JobSearchFunction.Models;
using Microsoft.Extensions.Logging;

namespace JobSearchFunction.Scrapers;

/// <summary>
/// Scrapes work.ua remote .NET developer vacancies posted in the last 24 hours.
/// period=1 → last day; notitle=1 → search in full description too.
/// </summary>
public sealed class WorkUaScraper(
    IHttpClientFactory httpClientFactory,
    ILogger<WorkUaScraper> logger) : ScraperBase, IJobScraper
{
    private const string BaseUrl = "https://www.work.ua";

    public string SourceName => "Work.ua";

    private static readonly string[] SearchUrls =
    [
        $"{BaseUrl}/jobs-remote-.net-developer/?notitle=1&period=1",
        $"{BaseUrl}/jobs-remote-angular-developer/?notitle=1&period=1"
    ];

    public async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(CancellationToken ct = default)
    {
        var results = new List<JobVacancy>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var client  = httpClientFactory.CreateClient("JobSearch");

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
                logger.LogWarning(ex, "[Work.ua] HTTP error for {Url}", url);
            }
        }

        logger.LogInformation("[Work.ua] Found {Count} unique vacancies", results.Count);
        return results;
    }

    // ── HTML parsing ──────────────────────────────────────────────────────────
    // Work.ua job cards: <div class="card card-hover card-visited job-link">
    //   <h2><a href="/jobs/123/">Title</a></h2>
    //   <div class="add-top-xs"> ... company name ... </div>
    //   <p class="text-muted"> ... salary ... </p>
    //   <p> ... description snippet ... </p>

    private IEnumerable<JobVacancy> ParsePage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Work.ua marks each job card with the class "job-link"
        var items = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'job-link')]");

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
            // Title link — could be h2>a or h3>a depending on page variant
            var titleNode = item.SelectSingleNode(".//h2//a")
                         ?? item.SelectSingleNode(".//h3//a");
            if (titleNode is null) return null;

            var title  = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
            var relUrl = titleNode.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrEmpty(relUrl)) return null;
            var url = relUrl.StartsWith("http") ? relUrl : $"{BaseUrl}{relUrl}";

            var company = HtmlEntity.DeEntitize(
                item.SelectSingleNode(
                    ".//div[contains(@class,'add-top-xs')]//a |" +
                    " .//span[contains(@class,'company-name')]")
                ?.InnerText ?? "N/A").Trim();

            var salary = item
                .SelectSingleNode(".//p[contains(@class,'text-muted')]//b")
                ?.InnerText.Trim();

            var description = HtmlEntity.DeEntitize(
                item.SelectSingleNode(".//p[not(@class) and not(@id)]")
                    ?.InnerText ?? string.Empty).Trim();

            var fullText = $"{title} {company} {description}";

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
                Description    = description
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Work.ua] Failed to parse item");
            return null;
        }
    }
}

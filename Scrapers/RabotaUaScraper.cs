using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobSearchFunction.Models;
using Microsoft.Extensions.Logging;

namespace JobSearchFunction.Scrapers;

/// <summary>
/// Queries the public Robota.ua JSON API.
/// scheduleId = 3 → Remote.
/// period     = 1 → last 24 hours.
/// If the API contract changes, adjust the DTO records below.
/// Fallback: if the API returns a non-success status, the scraper logs a
/// warning and returns an empty list (does not throw).
/// </summary>
public sealed class RabotaUaScraper(
    IHttpClientFactory httpClientFactory,
    ILogger<RabotaUaScraper> logger) : ScraperBase, IJobScraper
{
    private const string ApiUrl = "https://api.robota.ua/vacancy/search";

    public string SourceName => "Robota.ua";

    private static readonly object[] SearchRequests =
    [
        new { keywords = ".NET C# Angular Senior",  scheduleId = 3, period = 1, count = 20, page = 0 },
        new { keywords = ".NET C# Fullstack Middle", scheduleId = 3, period = 1, count = 20, page = 0 }
    ];

    public async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(CancellationToken ct = default)
    {
        var results = new List<JobVacancy>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var client  = httpClientFactory.CreateClient("JobSearch");

        foreach (var body in SearchRequests)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(1_500), ct);

            try
            {
                var response = await client.PostAsJsonAsync(ApiUrl, body, ct);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("[Robota.ua] API returned {Status}", response.StatusCode);
                    continue;
                }

                var data = await response.Content
                    .ReadFromJsonAsync<RobotaApiResponse>(cancellationToken: ct);

                if (data?.Documents is null) continue;

                foreach (var doc in data.Documents)
                {
                    var vacancy = Map(doc);
                    if (seen.Add(vacancy.Url))
                        results.Add(vacancy);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "[Robota.ua] Request failed");
            }
        }

        logger.LogInformation("[Robota.ua] Found {Count} unique vacancies", results.Count);
        return results;
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private JobVacancy Map(RobotaVacancyDoc doc)
    {
        var fullText = $"{doc.Name} {doc.Description}";
        var truncatedDesc = doc.Description is { Length: > 0 }
            ? doc.Description[..Math.Min(500, doc.Description.Length)]
            : string.Empty;

        return new JobVacancy
        {
            Title          = doc.Name ?? string.Empty,
            Company        = doc.CompanyName ?? "N/A",
            Source         = SourceName,
            Url            = $"https://robota.ua/ua/job/{doc.Id}",
            Salary         = FormatSalary(doc.SalaryFrom, doc.SalaryTo),
            WorkFormat     = WorkFormat.Remote, // already filtered by scheduleId=3
            EmploymentType = DetectEmploymentType(fullText),
            HasReservation = HasReservationMention(fullText),
            MatchedSkills  = ExtractMatchedSkills(fullText),
            SeniorityLevel = DetectSeniority(doc.Name ?? string.Empty),
            Description    = truncatedDesc
        };
    }

    private static string? FormatSalary(int? from, int? to) =>
        (from, to) switch
        {
            (null, null)   => null,
            ({ } f, null)  => $"від {f:N0} грн",
            (null, { } t)  => $"до {t:N0} грн",
            ({ } f, { } t) => $"{f:N0} – {t:N0} грн"
        };

    // ── Robota.ua API DTOs ────────────────────────────────────────────────────

    private sealed record RobotaApiResponse(
        [property: JsonPropertyName("documents")] List<RobotaVacancyDoc>? Documents,
        [property: JsonPropertyName("total")]     int Total);

    private sealed record RobotaVacancyDoc(
        [property: JsonPropertyName("id")]          int     Id,
        [property: JsonPropertyName("name")]        string? Name,
        [property: JsonPropertyName("companyName")] string? CompanyName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("salaryFrom")]  int?    SalaryFrom,
        [property: JsonPropertyName("salaryTo")]    int?    SalaryTo);
}

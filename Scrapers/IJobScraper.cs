using JobSearchFunction.Models;

namespace JobSearchFunction.Scrapers;

public interface IJobScraper
{
    string SourceName { get; }
    Task<IReadOnlyList<JobVacancy>> ScrapeAsync(CancellationToken ct = default);
}

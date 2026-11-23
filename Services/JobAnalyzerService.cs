using JobSearchFunction.Models;

namespace JobSearchFunction.Services;

/// <summary>
/// Scores each vacancy on a 100-point scale and returns the top N results.
///
/// Score breakdown:
///   Stack match       0 – 50 pts  (weighted skill presence)
///   Work format       0 – 20 pts  (Remote > Hybrid > Unknown > Office)
///   Reservation       0 – 15 pts  (binary bonus)
///   Seniority match   0 – 10 pts  (Senior / Lead best fit)
///   Employment type   0 –  5 pts  (FOP > Gig > KZoT > Unknown)
///   ─────────────────────────────
///   Maximum           0 – 100 pts
/// </summary>
public sealed class JobAnalyzerService
{
    // Weights for individual skills — tune to personal priorities
    private static readonly Dictionary<string, int> SkillWeights =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Core stack (high weight)
            ["c#"]              = 10,
            [".net"]            = 9,
            ["asp.net core"]    = 8,
            ["angular"]         = 9,
            ["typescript"]      = 7,
            ["sql server"]      = 7,
            ["mssql"]           = 5,
            ["azure"]           = 7,

            // Important extras
            ["docker"]              = 4,
            ["ci/cd"]               = 3,
            ["entity framework"]    = 4,
            ["ef core"]             = 4,
            ["microservices"]       = 4,
            ["rest api"]            = 3,
            ["signalr"]             = 5,
            ["clean architecture"]  = 5,
            ["tdd"]                 = 4,
            ["xunit"]               = 3,

            // Cloud & infrastructure
            ["azure functions"] = 4,
            ["service bus"]     = 3,
            ["app service"]     = 3,
            ["aks"]             = 3,
            ["kubernetes"]      = 3,
            ["github actions"]  = 3,

            // Bonus (AI/modern tools)
            ["github copilot"]  = 5,
            ["llm"]             = 4,
            ["openai"]          = 3,
            ["redis"]           = 2,
            ["rabbitmq"]        = 2,
            ["grpc"]            = 2,
        };

    // ── Public API ────────────────────────────────────────────────────────────

    public IReadOnlyList<JobVacancy> AnalyzeAndRank(
        IEnumerable<JobVacancy> vacancies,
        int topN = 10)
    {
        var list = vacancies.ToList();

        foreach (var v in list)
            v.RelevanceScore = CalculateScore(v);

        return list
            .Where(v => v.RelevanceScore >= 10)           // discard clearly irrelevant
            .Where(v => v.WorkFormat != WorkFormat.Office) // office-only never matches
            .OrderByDescending(v => v.RelevanceScore)
            .Take(topN)
            .ToList();
    }

    /// <summary>Generates a human-readable summary line for the email header.</summary>
    public string GenerateSummary(IReadOnlyList<JobVacancy> top)
    {
        if (top.Count == 0)
            return "Сьогодні релевантних вакансій не знайдено.";

        var remote      = top.Count(v => v.WorkFormat == WorkFormat.Remote);
        var withBronь   = top.Count(v => v.HasReservation);
        var withSalary  = top.Count(v => v.Salary is not null);
        var avgScore    = (int)top.Average(v => v.RelevanceScore);

        var topSkills = top
            .SelectMany(v => v.MatchedSkills)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(6)
            .Select(g => g.Key);

        var bySrc = top
            .GroupBy(v => v.Source)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} ({g.Count()})");

        return string.Join("\n",
        [
            $"📋 Знайдено: {top.Count}  |  🌐 Remote: {remote}  |  🛡 З бронею: {withBronь}  |  💰 Із зарплатою: {withSalary}",
            $"🎯 Середній score: {avgScore}/100",
            $"🔧 Топ навичок: {string.Join(", ", topSkills)}",
            $"📍 По джерелах: {string.Join("  |  ", bySrc)}"
        ]);
    }

    // ── Scoring logic ─────────────────────────────────────────────────────────

    private static int CalculateScore(JobVacancy v)
    {
        var score = 0;

        // 1. Stack match (cap at 50)
        var rawSkill = v.MatchedSkills
            .Sum(s => SkillWeights.TryGetValue(s, out var w) ? w : 1);
        score += Math.Min(50, rawSkill);

        // 2. Work format (0–20)
        score += v.WorkFormat switch
        {
            WorkFormat.Remote     => 20,
            WorkFormat.HybridKyiv =>  8,
            WorkFormat.Unknown    =>  5,
            WorkFormat.Office     =>  0,
            _                     =>  0
        };

        // 3. Reservation bonus (0–15)
        if (v.HasReservation) score += 15;

        // 4. Seniority match (0–10)
        score += v.SeniorityLevel switch
        {
            "Senior"      => 10,
            "Lead"        =>  8,
            "Middle"      =>  6,
            "Unspecified" =>  5,
            _             =>  2
        };

        // 5. Employment type (0–5)
        score += v.EmploymentType switch
        {
            EmploymentType.Fop            => 5,
            EmploymentType.GigContract    => 4,
            EmploymentType.KzotEmployment => 3,
            _                             => 2
        };

        return score;
    }
}

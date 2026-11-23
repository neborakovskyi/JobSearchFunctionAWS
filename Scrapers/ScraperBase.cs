using JobSearchFunction.Models;

namespace JobSearchFunction.Scrapers;

/// <summary>
/// Shared keyword-detection helpers. All scrapers inherit this class.
/// Adjust keyword lists here if site language or terminology changes.
/// </summary>
public abstract class ScraperBase
{
    // ── Skill detection ────────────────────────────────────────────────────────
    private static readonly string[] CoreSkills =
    [
        "c#", ".net", "asp.net core", "asp.net",
        "angular", "typescript",
        "sql server", "t-sql", "tsql", "mssql",
        "azure", "azure functions", "service bus", "app service", "aks",
        "docker", "kubernetes", "ci/cd", "github actions",
        "entity framework", "ef core",
        "signalr", "clean architecture", "tdd", "xunit",
        "microservices", "rest api", "grpc",
        "redis", "rabbitmq",
        "github copilot", "llm", "openai"
    ];

    // ── Work format keywords ───────────────────────────────────────────────────
    private static readonly string[] RemoteKeywords =
        ["remote", "remotely", "remote-first", "повністю remote",
         "дистанційно", "удалённо", "work from anywhere", "fully remote"];

    private static readonly string[] HybridKeywords =
        ["hybrid", "гібрид", "гибрид", "partial remote", "офіс/remote"];

    private static readonly string[] OfficeKeywords =
        ["office only", "в офісі", "в офисе", "only office", "onsite only"];

    // ── Reservation keywords ──────────────────────────────────────────────────
    private static readonly string[] ReservationKeywords =
    [
        "броню", "бронювання", "бронирован", "бронь",
        "відстрочк", "відстрочен",
        "military deferment", "deferment", "reservation"
    ];

    // ── Employment type keywords ───────────────────────────────────────────────
    private static readonly string[] FopKeywords =
        ["фоп", "фізична особа-підприємець", "individual entrepreneur", "self-employed", "пп"];

    private static readonly string[] GigKeywords =
        ["гіг", "gig contract", "гиг-контракт", "gig-"];

    private static readonly string[] KzotKeywords =
        ["кзот", "кзпп", "трудова книжка", "штатна посада",
         "full-time employment", "трудовий договір"];

    // ── Public helpers ─────────────────────────────────────────────────────────

    protected static WorkFormat DetectWorkFormat(string text)
    {
        var t = text.ToLowerInvariant();
        if (RemoteKeywords.Any(t.Contains))  return WorkFormat.Remote;
        if (HybridKeywords.Any(t.Contains))  return WorkFormat.HybridKyiv;
        if (OfficeKeywords.Any(t.Contains))  return WorkFormat.Office;
        return WorkFormat.Unknown;
    }

    protected static EmploymentType DetectEmploymentType(string text)
    {
        var t = text.ToLowerInvariant();
        if (FopKeywords.Any(t.Contains))   return EmploymentType.Fop;
        if (GigKeywords.Any(t.Contains))   return EmploymentType.GigContract;
        if (KzotKeywords.Any(t.Contains))  return EmploymentType.KzotEmployment;
        return EmploymentType.Unknown;
    }

    protected static bool HasReservationMention(string text)
    {
        var t = text.ToLowerInvariant();
        return ReservationKeywords.Any(t.Contains);
    }

    protected static string DetectSeniority(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("lead") || t.Contains("principal") || t.Contains("staff"))
            return "Lead";
        if (t.Contains("senior") || t.Contains("sr.") || t.StartsWith("sr "))
            return "Senior";
        if (t.Contains("middle") || t.Contains("mid-level"))
            return "Middle";
        if (t.Contains("junior") || t.Contains("jr."))
            return "Junior";
        return "Unspecified";
    }

    protected static List<string> ExtractMatchedSkills(string text)
    {
        var t = text.ToLowerInvariant();
        return CoreSkills.Where(s => t.Contains(s)).ToList();
    }
}

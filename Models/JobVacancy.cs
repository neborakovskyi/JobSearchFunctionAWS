namespace JobSearchFunction.Models;

public sealed record JobVacancy
{
    public string Title          { get; init; } = string.Empty;
    public string Company        { get; init; } = string.Empty;
    public string Source         { get; init; } = string.Empty;
    public string Url            { get; init; } = string.Empty;
    public string? Salary        { get; init; }
    public string? SeniorityLevel{ get; init; }
    public string Description    { get; init; } = string.Empty;
    public WorkFormat       WorkFormat      { get; init; }
    public EmploymentType   EmploymentType  { get; init; }
    public bool             HasReservation  { get; init; }
    public List<string>     MatchedSkills   { get; init; } = [];

    // Mutable — set by JobAnalyzerService after construction
    public int RelevanceScore { get; set; }
}

public enum WorkFormat
{
    Remote,
    HybridKyiv,
    Office,
    Unknown
}

public enum EmploymentType
{
    Fop,
    GigContract,
    KzotEmployment,
    Unknown
}

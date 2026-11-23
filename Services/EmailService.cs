using JobSearchFunction.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.Text;

namespace JobSearchFunction.Services;

/// <summary>
/// Sends the daily job report via Gmail SMTP.
/// Requires an App Password (not the regular Gmail password).
/// Generate one at: https://myaccount.google.com/apppasswords
/// </summary>
public sealed class EmailService(
    IConfiguration configuration,
    ILogger<EmailService> logger)
{
    // LinkedIn manual search link — last 24 h, remote, Ukraine
    private const string LinkedInUrl =
        "https://www.linkedin.com/jobs/search/" +
        "?keywords=.NET%20C%23%20Angular%20Senior%20Developer" +
        "&location=Ukraine&f_TPR=r86400&f_WT=2&sortBy=DD";

    public async Task SendReportAsync(
        IReadOnlyList<JobVacancy> vacancies,
        string summary,
        CancellationToken ct = default)
    {
        var smtpUser  = configuration["GmailSmtpUser"]!;
        var smtpPass  = configuration["GmailSmtpPassword"]!;
        var recipient = configuration["RecipientEmail"]!;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Job Search Bot", smtpUser));
        message.To.Add(new MailboxAddress("Ростислав", recipient));
        message.Subject =
            $"🔍 Job Report {DateTime.Now:dd.MM.yyyy} — {vacancies.Count} вакансій";

        message.Body = new BodyBuilder
        {
            HtmlBody = BuildHtml(vacancies, summary)
        }.ToMessageBody();

        try
        {
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, ct);
            await smtp.AuthenticateAsync(smtpUser, smtpPass, ct);
            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(quit: true, ct);

            logger.LogInformation("Email sent: {Count} vacancies", vacancies.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email");
            throw;
        }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private static string BuildHtml(IReadOnlyList<JobVacancy> vacancies, string summary)
    {
        var sb = new StringBuilder();

        sb.Append("""
            <!DOCTYPE html>
            <html lang="uk">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
            </head>
            <body style="margin:0;padding:20px;background:#f0f4f8;font-family:'Segoe UI',Arial,sans-serif;">
            <div style="max-width:960px;margin:0 auto;background:#fff;border-radius:10px;
                        overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,.12);">
            """);

        // ── Header ──────────────────────────────────────────────────────────
        sb.Append($"""
            <div style="background:linear-gradient(135deg,#1a365d,#2b6cb0);
                        color:#fff;padding:28px 36px;">
              <h1 style="margin:0;font-size:22px;letter-spacing:.3px;">
                🔍 Daily Job Report — Middle+ / Senior .NET / Fullstack
              </h1>
              <p style="margin:8px 0 0;opacity:.8;font-size:13px;">
                {DateTime.Now:dddd, dd MMMM yyyy} · C# · Angular · Azure · SQL Server
              </p>
            </div>
            """);

        // ── Summary block ────────────────────────────────────────────────────
        sb.Append("<div style=\"background:#ebf4ff;padding:16px 36px;border-bottom:1px solid #bee3f8;\">");
        foreach (var line in summary.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            sb.Append($"<p style=\"margin:3px 0;font-size:13px;color:#2c5282;\">{line}</p>");
        sb.Append("</div>");

        // ── LinkedIn manual link ─────────────────────────────────────────────
        sb.Append($"""
            <div style="background:#fffbeb;padding:12px 36px;
                        border-left:4px solid #f6ad55;border-bottom:1px solid #feebc8;">
              <span style="font-size:13px;font-weight:600;color:#744210;">🔗 LinkedIn (ручний пошук):</span>
              <a href="{LinkedInUrl}"
                 style="color:#2b6cb0;font-size:13px;margin-left:8px;text-decoration:none;">
                Відкрити пошук →
              </a>
              <span style="display:block;margin-top:3px;font-size:11px;color:#975a16;">
                Senior .NET C# Angular | Ukraine | Remote | Last 24 h | Sorted by date
              </span>
            </div>
            """);

        // ── Vacancy table ────────────────────────────────────────────────────
        sb.Append("""
            <div style="padding:28px 36px;">
              <h2 style="margin:0 0 18px;font-size:15px;color:#1a365d;">
                📋 Вакансії — відсортовано за релевантністю
              </h2>
              <table style="width:100%;border-collapse:collapse;font-size:13px;">
                <thead>
                  <tr style="background:#f7fafc;">
                    <th style="text-align:left;padding:10px 8px;color:#718096;
                               font-size:11px;font-weight:600;border-bottom:2px solid #e2e8f0;
                               min-width:220px;">ПОЗИЦІЯ / КОМПАНІЯ</th>
                    <th style="text-align:left;padding:10px 8px;color:#718096;
                               font-size:11px;font-weight:600;border-bottom:2px solid #e2e8f0;">ЗАРПЛАТА</th>
                    <th style="text-align:left;padding:10px 8px;color:#718096;
                               font-size:11px;font-weight:600;border-bottom:2px solid #e2e8f0;">ФОРМАТ</th>
                    <th style="text-align:left;padding:10px 8px;color:#718096;
                               font-size:11px;font-weight:600;border-bottom:2px solid #e2e8f0;">ОФОРМЛЕННЯ</th>
                    <th style="text-align:left;padding:10px 8px;color:#718096;
                               font-size:11px;font-weight:600;border-bottom:2px solid #e2e8f0;
                               min-width:160px;">СТЕК (збіг)</th>
                    <th style="text-align:center;padding:10px 8px;color:#718096;
                               font-size:11px;font-weight:600;border-bottom:2px solid #e2e8f0;">SCORE</th>
                  </tr>
                </thead>
                <tbody>
            """);

        for (var i = 0; i < vacancies.Count; i++)
            sb.Append(BuildRow(vacancies[i], i));

        sb.Append("""
                </tbody>
              </table>
            </div>
            """);

        // ── Footer ───────────────────────────────────────────────────────────
        sb.Append($"""
            <div style="background:#f7fafc;padding:14px 36px;text-align:center;
                        color:#a0aec0;font-size:11px;border-top:1px solid #e2e8f0;">
              Автоматичний звіт · Azure Function Timer Trigger · 09:30 Kyiv ·
              Djinni · DOU · Work.ua · Robota.ua · {DateTime.Now:HH:mm} UTC+2
            </div>
            """);

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static string BuildRow(JobVacancy v, int index)
    {
        var rowBg = index % 2 == 0 ? "#ffffff" : "#f9fafb";

        var scoreColor = v.RelevanceScore >= 70 ? "#276749"
                       : v.RelevanceScore >= 50 ? "#c05621"
                       : "#c53030";
        var scoreBg    = v.RelevanceScore >= 70 ? "#f0fff4"
                       : v.RelevanceScore >= 50 ? "#fffaf0"
                       : "#fff5f5";

        var formatBadge = v.WorkFormat switch
        {
            WorkFormat.Remote =>
                Badge("#c6f6d5", "#276749", "🌐 Remote"),
            WorkFormat.HybridKyiv =>
                Badge("#fefcbf", "#744210", "🏙 Hybrid Kyiv"),
            _ =>
                Badge("#edf2f7", "#718096", "❓ N/A")
        };

        var reservTag = v.HasReservation
            ? $"<br><span style=\"display:inline-block;margin-top:4px;" +
              $"background:#fff5f5;color:#9b2335;padding:2px 7px;border-radius:10px;" +
              $"font-size:10px;font-weight:700;\">🛡 Є бронь</span>"
            : string.Empty;

        var empBadge = v.EmploymentType switch
        {
            EmploymentType.Fop            => Badge("#e9d8fd", "#6b46c1", "ФОП"),
            EmploymentType.GigContract    => Badge("#bee3f8", "#2c5282", "Гіг"),
            EmploymentType.KzotEmployment => Badge("#c6f6d5", "#276749", "КЗпП"),
            _                             => "<span style=\"color:#a0aec0;font-size:11px;\">—</span>"
        };

        var skills = string.Join(" ", v.MatchedSkills.Take(5)
            .Select(s => $"<span style=\"background:#edf2f7;color:#4a5568;padding:1px 5px;" +
                         $"border-radius:3px;font-size:10px;\">{s}</span>"));

        var source = $"<span style=\"color:#a0aec0;font-size:10px;\">[{v.Source}]</span>";

        return $"""
            <tr style="background:{rowBg};">
              <td style="padding:13px 8px;border-bottom:1px solid #edf2f7;vertical-align:top;">
                <a href="{v.Url}" style="color:#1a365d;font-weight:600;font-size:13px;
                   text-decoration:none;">{v.Title}</a><br>
                <span style="color:#718096;font-size:12px;">{v.Company}</span><br>
                {source}
              </td>
              <td style="padding:13px 8px;border-bottom:1px solid #edf2f7;
                         white-space:nowrap;color:#2d3748;vertical-align:top;">
                {(v.Salary ?? "<span style='color:#a0aec0;'>—</span>")}
              </td>
              <td style="padding:13px 8px;border-bottom:1px solid #edf2f7;vertical-align:top;">
                {formatBadge}{reservTag}
              </td>
              <td style="padding:13px 8px;border-bottom:1px solid #edf2f7;vertical-align:top;">
                {empBadge}
              </td>
              <td style="padding:13px 8px;border-bottom:1px solid #edf2f7;vertical-align:top;">
                {skills}
              </td>
              <td style="padding:13px 8px;border-bottom:1px solid #edf2f7;
                         text-align:center;vertical-align:top;">
                <span style="font-size:17px;font-weight:800;color:{scoreColor};
                             background:{scoreBg};padding:3px 8px;border-radius:6px;">
                  {v.RelevanceScore}
                </span>
              </td>
            </tr>
            """;
    }

    private static string Badge(string bg, string color, string text) =>
        $"<span style=\"background:{bg};color:{color};padding:2px 8px;" +
        $"border-radius:12px;font-size:11px;font-weight:600;\">{text}</span>";
}

using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;

namespace QrDine.Application.Feedback;

// MemberToken (optional): a loyalty member rating from their phone can earn the feedback bonus.
public record FeedbackInput(int Rating, string? Comment, string? TableCode, string? MemberToken = null);
public record FeedbackDto(int Id, int Rating, string Comment, string TableCode, DateTime CreatedAt);
public record FeedbackSummaryDto(double Average, int Count, IReadOnlyList<int> Distribution, IReadOnlyList<FeedbackDto> Recent);

/// <summary>Customer feedback: guests submit a rating from their table's page; the
/// owner sees the average, the star distribution and recent comments.</summary>
public class FeedbackService
{
    private readonly IAppDbContext _db;
    public FeedbackService(IAppDbContext db) => _db = db;

    public async Task Submit(FeedbackInput req, CancellationToken ct = default)
    {
        if (req.Rating is < 1 or > 5) throw new DomainException("Rating must be between 1 and 5.");
        _db.Feedbacks.Add(new Domain.Feedback.Feedback
        {
            Rating = req.Rating,
            Comment = (req.Comment ?? "").Trim(),
            TableCode = (req.TableCode ?? "").Trim(),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);   // RestaurantId is stamped from the tenant in context
    }

    public async Task<FeedbackSummaryDto> GetSummary(CancellationToken ct = default)
    {
        var all = await _db.Feedbacks.AsNoTracking().OrderByDescending(f => f.CreatedAt).ToListAsync(ct);
        var dist = new int[5];
        foreach (var f in all) if (f.Rating is >= 1 and <= 5) dist[f.Rating - 1]++;
        var avg = all.Count == 0 ? 0 : Math.Round(all.Average(f => f.Rating), 2);
        var recent = all.Where(f => !string.IsNullOrWhiteSpace(f.Comment)).Take(20)
            .Select(f => new FeedbackDto(f.Id, f.Rating, f.Comment, f.TableCode, f.CreatedAt)).ToList();
        return new FeedbackSummaryDto(avg, all.Count, dist, recent);
    }
}


namespace DoubtConnect.Domain.Entities;

public class MentorReview
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Guid StudentId { get; set; }

    public Guid MentorId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

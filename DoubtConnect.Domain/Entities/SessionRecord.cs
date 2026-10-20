
namespace DoubtConnect.Domain.Entities;

public class SessionRecord
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Guid StudentId { get; set; }

    public Guid MentorId { get; set; }

    public string DoubtTitle { get; set; } = string.Empty;

    public DateTime ScheduledStartUtc { get; set; }

    public DateTime ActualStartUtc { get; set; }

    public DateTime ActualEndUtc { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

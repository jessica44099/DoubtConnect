
namespace DoubtConnect.Domain.Entities;

public class MentorCalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MentorId { get; set; }

    public DateOnly EventDate { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public string ActivityType { get; set; } = "Meeting";

    public string Title { get; set; } = string.Empty;

    public string? StudentName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public ApplicationUser Mentor { get; set; } = null!;
}

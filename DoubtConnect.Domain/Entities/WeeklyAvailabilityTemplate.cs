
namespace DoubtConnect.Domain.Entities;

public class WeeklyAvailabilityTemplate
{
    public Guid Id { get; set; }

    public Guid MentorId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    // True means students may book this time, unless a booking conflicts.
    public bool IsFree { get; set; } = true;

    // Examples: Free, Meeting, Class, Break, Personal, Leave.
    public string ActivityType { get; set; } = "Free";

    // Optional details, such as "Team meeting" or "Lunch break".
    public string? Title { get; set; }

    public ApplicationUser Mentor { get; set; } = null!;
}

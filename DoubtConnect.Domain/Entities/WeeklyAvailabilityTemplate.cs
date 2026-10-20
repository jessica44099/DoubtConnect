
namespace DoubtConnect.Domain.Entities;

public class WeeklyAvailabilityTemplate
{
    public Guid Id { get; set; }

    public Guid MentorId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public bool IsFree { get; set; } = true;

    public ApplicationUser Mentor { get; set; } = null!;
}

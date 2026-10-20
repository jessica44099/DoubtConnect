
namespace DoubtConnect.Domain.Entities;

public class StudentProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? EducationLevel { get; set; }

    public int TotalSessionsAttended { get; set; }

    public ApplicationUser User { get; set; } = null!;
}

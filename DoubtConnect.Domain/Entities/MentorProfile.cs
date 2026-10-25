
namespace DoubtConnect.Domain.Entities;

public class MentorProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Expertise { get; set; } = string.Empty;

    public string? Bio { get; set; }

    public decimal? HourlyRate { get; set; }

    public int TotalDoubtsCleared { get; set; }

    // Professional profile information.
    public string? Education { get; set; }

    public string? Certifications { get; set; }

    public string? Achievements { get; set; }

    public ApplicationUser User { get; set; } = null!;
}

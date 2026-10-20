
using DoubtConnect.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace DoubtConnect.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public MentorProfile? MentorProfile { get; set; }

    public StudentProfile? StudentProfile { get; set; }
}

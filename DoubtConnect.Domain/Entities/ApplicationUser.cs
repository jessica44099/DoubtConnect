
using DoubtConnect.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace DoubtConnect.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Soft deletion preserves historical bookings and session records.
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAtUtc { get; set; }

    public MentorProfile? MentorProfile { get; set; }

    public StudentProfile? StudentProfile { get; set; }
}

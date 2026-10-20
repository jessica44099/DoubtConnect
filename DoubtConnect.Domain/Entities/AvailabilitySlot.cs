
using DoubtConnect.Domain.Enums;

namespace DoubtConnect.Domain.Entities;

public class AvailabilitySlot
{
    public Guid Id { get; set; }

    public Guid MentorId { get; set; }

    public DateTime StartTimeUtc { get; set; }

    public DateTime EndTimeUtc { get; set; }

    public AvailabilityStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ApplicationUser Mentor { get; set; } = null!;
}

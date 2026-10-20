
using DoubtConnect.Domain.Enums;

namespace DoubtConnect.Domain.Entities;

public class Booking
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public Guid MentorId { get; set; }

    public DateTime SlotStartUtc { get; set; }

    public DateTime SlotEndUtc { get; set; }

    public SessionMode Mode { get; set; }

    public string DoubtTitle { get; set; } = string.Empty;

    public string DoubtDescription { get; set; } = string.Empty;

    public string? AttachmentUrl { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public string? MeetingLink { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime? ActualStartUtc { get; set; }

    public DateTime? ActualEndUtc { get; set; }

    public Guid? ShiftedFromBookingId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public ApplicationUser Student { get; set; } = null!;

    public ApplicationUser Mentor { get; set; } = null!;

    public Booking? ShiftedFromBooking { get; set; }

    public ICollection<Booking> ShiftedBookings { get; set; } =
        new List<Booking>();
}

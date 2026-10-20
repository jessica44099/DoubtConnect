
namespace DoubtConnect.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Guid SenderId { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsRead { get; set; }

    public Booking Booking { get; set; } = null!;

    public ApplicationUser Sender { get; set; } = null!;
}

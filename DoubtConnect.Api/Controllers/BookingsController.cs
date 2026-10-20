
using System.Security.Claims;
using DoubtConnect.Domain.Entities;
using DoubtConnect.Domain.Enums;
using DoubtConnect.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoubtConnect.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public BookingsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // Student requests a session for a free availability slot.
    [HttpPost]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> CreateBooking(
        CreateBookingRequest request)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized();

        if (!Enum.IsDefined(typeof(SessionMode), request.Mode))
            return BadRequest(new { message = "Invalid session mode." });

        if (string.IsNullOrWhiteSpace(request.DoubtTitle) ||
            string.IsNullOrWhiteSpace(request.DoubtDescription))
        {
            return BadRequest(new
            {
                message = "Doubt title and description are required."
            });
        }

        if (request.DoubtTitle.Length > 200 ||
            request.DoubtDescription.Length > 5000)
        {
            return BadRequest(new
            {
                message = "Title must be at most 200 characters and description at most 5000."
            });
        }

        var slot = await _context.AvailabilitySlots
            .FirstOrDefaultAsync(s =>
                s.Id == request.AvailabilitySlotId &&
                s.Status == AvailabilityStatus.Free);

        if (slot == null)
            return NotFound(new { message = "Free availability slot not found." });

        if (slot.StartTimeUtc <= DateTime.UtcNow ||
            slot.EndTimeUtc <= slot.StartTimeUtc)
        {
            return BadRequest(new { message = "This slot is no longer bookable." });
        }

        if (slot.MentorId == studentId)
            return BadRequest(new { message = "You cannot book your own slot." });

        // Prevent double booking by checking active overlapping bookings.
        var conflict = await _context.Bookings.AnyAsync(b =>
            b.MentorId == slot.MentorId &&
            b.SlotStartUtc < slot.EndTimeUtc &&
            b.SlotEndUtc > slot.StartTimeUtc &&
            (b.Status == BookingStatus.Pending ||
             b.Status == BookingStatus.Approved ||
             b.Status == BookingStatus.Ongoing ||
             b.Status == BookingStatus.Rescheduled ||
             b.Status == BookingStatus.NeedsReschedule));

        if (conflict)
            return Conflict(new { message = "This slot already has a conflicting booking." });

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            MentorId = slot.MentorId,
            SlotStartUtc = slot.StartTimeUtc,
            SlotEndUtc = slot.EndTimeUtc,
            Mode = request.Mode,
            DoubtTitle = request.DoubtTitle.Trim(),
            DoubtDescription = request.DoubtDescription.Trim(),
            Status = BookingStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Reserve the slot immediately while the mentor reviews the request.
        slot.Status = AvailabilityStatus.Busy;

        _context.Bookings.Add(booking);

        _context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = slot.MentorId,
            Title = "New booking request",
            Message = $"A student requested a session: {booking.DoubtTitle}",
            Link = $"/bookings/{booking.Id}",
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetBookingById),
            new { id = booking.Id },
            new
            {
                booking.Id,
                booking.StudentId,
                booking.MentorId,
                booking.SlotStartUtc,
                booking.SlotEndUtc,
                booking.Mode,
                booking.DoubtTitle,
                booking.DoubtDescription,
                booking.Status
            });
    }

    // Student or mentor can see their own bookings.
    [HttpGet("mine")]
    public async Task<IActionResult> GetMyBookings()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var bookings = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.StudentId == userId || b.MentorId == userId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new
            {
                b.Id,
                b.StudentId,
                StudentName = b.Student.FullName,
                b.MentorId,
                MentorName = b.Mentor.FullName,
                b.SlotStartUtc,
                b.SlotEndUtc,
                b.Mode,
                b.DoubtTitle,
                b.DoubtDescription,
                b.Status,
                b.MeetingLink,
                b.RejectionReason,
                b.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(bookings);
    }

    // Retrieve a booking only if the current user is its student or mentor.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBookingById(Guid id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var booking = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.Id == id &&
                (b.StudentId == userId || b.MentorId == userId))
            .Select(b => new
            {
                b.Id,
                b.StudentId,
                StudentName = b.Student.FullName,
                b.MentorId,
                MentorName = b.Mentor.FullName,
                b.SlotStartUtc,
                b.SlotEndUtc,
                b.Mode,
                b.DoubtTitle,
                b.DoubtDescription,
                b.Status,
                b.MeetingLink,
                b.RejectionReason,
                b.CreatedAtUtc
            })
            .FirstOrDefaultAsync();

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        return Ok(booking);
    }

    // Mentor accepts a pending request.
    [HttpPut("{id:guid}/approve")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> ApproveBooking(Guid id)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var booking = await _context.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.MentorId == mentorId);

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        if (booking.Status != BookingStatus.Pending)
            return Conflict(new { message = "Only pending bookings can be approved." });

        booking.Status = BookingStatus.Approved;
        booking.UpdatedAtUtc = DateTime.UtcNow;

        _context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = booking.StudentId,
            Title = "Booking approved",
            Message = $"Your session request '{booking.DoubtTitle}' was approved.",
            Link = $"/bookings/{booking.Id}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return Ok(new { message = "Booking approved.", booking.Id, booking.Status });
    }

    // Mentor rejects a pending request.
    [HttpPut("{id:guid}/reject")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> RejectBooking(
        Guid id,
        RejectBookingRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var booking = await _context.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.MentorId == mentorId);

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        if (booking.Status != BookingStatus.Pending)
            return Conflict(new { message = "Only pending bookings can be rejected." });

        booking.Status = BookingStatus.Rejected;
        booking.RejectionReason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason.Trim();
        booking.UpdatedAtUtc = DateTime.UtcNow;

        // Release the reserved availability slot if it still matches this booking.
        var slot = await _context.AvailabilitySlots.FirstOrDefaultAsync(s =>
            s.MentorId == mentorId &&
            s.StartTimeUtc == booking.SlotStartUtc &&
            s.EndTimeUtc == booking.SlotEndUtc);

        if (slot != null)
            slot.Status = AvailabilityStatus.Free;

        _context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = booking.StudentId,
            Title = "Booking rejected",
            Message = $"Your session request '{booking.DoubtTitle}' was rejected.",
            Link = $"/bookings/{booking.Id}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return Ok(new { message = "Booking rejected.", booking.Id, booking.Status });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }
}

public sealed class CreateBookingRequest
{
    public Guid AvailabilitySlotId { get; set; }
    public SessionMode Mode { get; set; }
    public string DoubtTitle { get; set; } = string.Empty;
    public string DoubtDescription { get; set; } = string.Empty;
}

public sealed class RejectBookingRequest
{
    public string? Reason { get; set; }
}


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

    [HttpPost]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> CreateBooking(
        [FromBody] CreateBookingRequest request)
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

        if (request.DoubtTitle.Trim().Length > 200 ||
            request.DoubtDescription.Trim().Length > 5000)
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

        var conflict = await _context.Bookings.AnyAsync(b =>
            b.MentorId == slot.MentorId &&
            b.SlotStartUtc < slot.EndTimeUtc &&
            b.SlotEndUtc > slot.StartTimeUtc &&
            IsActiveBookingStatus(b.Status));

        if (conflict)
        {
            return Conflict(new
            {
                message = "This slot already has a conflicting booking."
            });
        }

        var now = DateTime.UtcNow;

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
            CreatedAtUtc = now
        };

        slot.Status = AvailabilityStatus.Busy;
        _context.Bookings.Add(booking);

        AddNotification(
            slot.MentorId,
            "New booking request",
            $"A student requested a session: {booking.DoubtTitle}",
            $"/bookings/{booking.Id}");

        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetBookingById),
            new { id = booking.Id },
            ToBookingResponse(booking));
    }

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
                b.ActualStartUtc,
                b.ActualEndUtc,
                b.EstimatedEndUtc,
                b.ShiftedFromBookingId,
                b.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(bookings);
    }

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
                b.ActualStartUtc,
                b.ActualEndUtc,
                b.EstimatedEndUtc,
                b.ShiftedFromBookingId,
                b.CreatedAtUtc
            })
            .FirstOrDefaultAsync();

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        return Ok(booking);
    }

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

        AddNotification(
            booking.StudentId,
            "Booking approved",
            $"Your session request '{booking.DoubtTitle}' was approved.",
            $"/bookings/{booking.Id}");

        await _context.SaveChangesAsync();

        return Ok(new { message = "Booking approved.", booking.Id, booking.Status });
    }

    [HttpPut("{id:guid}/reject")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> RejectBooking(
        Guid id,
        [FromBody] RejectBookingRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var booking = await _context.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.MentorId == mentorId);

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        if (booking.Status != BookingStatus.Pending)
            return Conflict(new { message = "Only pending bookings can be rejected." });

        if (request.Reason?.Trim().Length > 1000)
            return BadRequest(new { message = "The rejection reason cannot exceed 1000 characters." });

        booking.Status = BookingStatus.Rejected;
        booking.RejectionReason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason.Trim();
        booking.UpdatedAtUtc = DateTime.UtcNow;

        await ReleaseMatchingSlotIfAvailable(booking);

        AddNotification(
            booking.StudentId,
            "Booking rejected",
            string.IsNullOrWhiteSpace(booking.RejectionReason)
                ? $"Your session request '{booking.DoubtTitle}' was rejected."
                : $"Your session request '{booking.DoubtTitle}' was rejected. Reason: {booking.RejectionReason}",
            $"/bookings/{booking.Id}");

        await _context.SaveChangesAsync();

        return Ok(new { message = "Booking rejected.", booking.Id, booking.Status });
    }

    // Mentor marks a booking as needing rescheduling.
    [HttpPut("{id:guid}/needs-reschedule")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> RequestReschedule(
        Guid id,
        [FromBody] RequestRescheduleRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var booking = await _context.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.MentorId == mentorId);

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        if (booking.Status != BookingStatus.Pending &&
            booking.Status != BookingStatus.Approved &&
            booking.Status != BookingStatus.Rescheduled)
        {
            return Conflict(new
            {
                message = "Only pending, approved, or rescheduled bookings can be flagged for rescheduling."
            });
        }

        if (request.Reason?.Trim().Length > 1000)
            return BadRequest(new { message = "The reason cannot exceed 1000 characters." });

        booking.Status = BookingStatus.NeedsReschedule;
        booking.UpdatedAtUtc = DateTime.UtcNow;

        AddNotification(
            booking.StudentId,
            "Session rescheduling required",
            string.IsNullOrWhiteSpace(request.Reason)
                ? $"Your session '{booking.DoubtTitle}' needs to be rescheduled."
                : $"Your session '{booking.DoubtTitle}' needs to be rescheduled. Reason: {request.Reason.Trim()}",
            $"/bookings/{booking.Id}");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "The booking has been marked for rescheduling.",
            booking.Id,
            booking.Status
        });
    }

    // Mentor creates a replacement booking while preserving the original.
    [HttpPut("{id:guid}/reschedule")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> RescheduleBooking(
        Guid id,
        [FromBody] RescheduleBookingRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var original = await _context.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.MentorId == mentorId);

        if (original == null)
            return NotFound(new { message = "Original booking not found." });

        if (original.Status != BookingStatus.NeedsReschedule &&
            original.Status != BookingStatus.Approved &&
            original.Status != BookingStatus.Rescheduled)
        {
            return Conflict(new
            {
                message = "Only an approved, rescheduled, or needs-reschedule booking can be moved."
            });
        }

        if (request.NewSlotStartUtc.Kind == DateTimeKind.Unspecified ||
            request.NewSlotEndUtc.Kind == DateTimeKind.Unspecified)
        {
            return BadRequest(new
            {
                message = "The new start and end times must include a timezone, preferably UTC using Z."
            });
        }

        var startUtc = request.NewSlotStartUtc.ToUniversalTime();
        var endUtc = request.NewSlotEndUtc.ToUniversalTime();

        if (startUtc <= DateTime.UtcNow || endUtc <= startUtc)
        {
            return BadRequest(new
            {
                message = "The replacement slot must be in the future and end after it starts."
            });
        }

        if (request.Mode.HasValue &&
            !Enum.IsDefined(typeof(SessionMode), request.Mode.Value))
        {
            return BadRequest(new { message = "Invalid session mode." });
        }

        var slot = await _context.AvailabilitySlots
            .FirstOrDefaultAsync(s =>
                s.MentorId == mentorId &&
                s.StartTimeUtc == startUtc &&
                s.EndTimeUtc == endUtc &&
                s.Status == AvailabilityStatus.Free);

        if (slot == null)
        {
            return Conflict(new
            {
                message = "The mentor does not have a matching free availability slot for that time."
            });
        }

        var conflict = await _context.Bookings.AnyAsync(b =>
            b.MentorId == mentorId &&
            b.Id != original.Id &&
            b.SlotStartUtc < endUtc &&
            b.SlotEndUtc > startUtc &&
            IsActiveBookingStatus(b.Status));

        if (conflict)
        {
            return Conflict(new
            {
                message = "The replacement time conflicts with another active booking."
            });
        }

        var replacement = new Booking
        {
            Id = Guid.NewGuid(),
            StudentId = original.StudentId,
            MentorId = original.MentorId,
            SlotStartUtc = startUtc,
            SlotEndUtc = endUtc,
            Mode = request.Mode ?? original.Mode,
            DoubtTitle = original.DoubtTitle,
            DoubtDescription = original.DoubtDescription,
            AttachmentUrl = original.AttachmentUrl,
            Status = BookingStatus.Rescheduled,
            ShiftedFromBookingId = original.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Keep the original booking as a historical record and mark it as moved.
        original.Status = BookingStatus.Cancelled;
        original.UpdatedAtUtc = DateTime.UtcNow;
        original.RejectionReason = "Rescheduled to a new booking.";

        await ReleaseMatchingSlotIfAvailable(original);

        slot.Status = AvailabilityStatus.Busy;
        _context.Bookings.Add(replacement);

        AddNotification(
            original.StudentId,
            "Session rescheduled",
            $"Your session '{original.DoubtTitle}' has been moved to {startUtc:dd MMM yyyy, hh:mm tt} UTC. The replacement booking is {replacement.Id}.",
            $"/bookings/{replacement.Id}");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Session rescheduled successfully.",
            originalBookingId = original.Id,
            original.Status,
            replacementBooking = ToBookingResponse(replacement)
        });
    }

    [HttpPut("{id:guid}/start")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> StartSession(Guid id)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var booking = await _context.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.MentorId == mentorId);

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        if (booking.Status != BookingStatus.Approved &&
            booking.Status != BookingStatus.Rescheduled)
        {
            return Conflict(new
            {
                message = "Only approved or rescheduled sessions can be started."
            });
        }

        if (booking.ActualStartUtc.HasValue)
            return Conflict(new { message = "A start time has already been recorded." });

        var now = DateTime.UtcNow;

        booking.Status = BookingStatus.Ongoing;
        booking.ActualStartUtc = now;
        booking.UpdatedAtUtc = now;

        AddNotification(
            booking.StudentId,
            "Your session has started",
            $"Your mentor has started the session '{booking.DoubtTitle}'.",
            $"/bookings/{booking.Id}");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Session started.",
            booking.Id,
            booking.Status,
            booking.ActualStartUtc
        });
    }

    [HttpPut("{id:guid}/running-late")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> ReportRunningLate(
        Guid id,
        [FromBody] ReportRunningLateRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var booking = await _context.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.MentorId == mentorId);

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        if (booking.Status != BookingStatus.Ongoing ||
            !booking.ActualStartUtc.HasValue)
        {
            return Conflict(new
            {
                message = "Only an ongoing session can be marked as running late."
            });
        }

        if (request.EstimatedEndUtc.Kind == DateTimeKind.Unspecified)
            return BadRequest(new { message = "EstimatedEndUtc must include a timezone indicator, preferably Z." });

        var estimatedEndUtc = request.EstimatedEndUtc.ToUniversalTime();

        if (estimatedEndUtc <= DateTime.UtcNow ||
            estimatedEndUtc <= booking.ActualStartUtc.Value)
        {
            return BadRequest(new
            {
                message = "The estimated end time must be in the future and after the session start."
            });
        }

        if (estimatedEndUtc <= booking.SlotEndUtc)
        {
            return BadRequest(new
            {
                message = "The estimated end time must be later than the scheduled session end."
            });
        }

        booking.EstimatedEndUtc = estimatedEndUtc;
        booking.UpdatedAtUtc = DateTime.UtcNow;

        var affectedBookings = await _context.Bookings
            .Where(b =>
                b.Id != booking.Id &&
                b.MentorId == mentorId &&
                b.SlotStartUtc < estimatedEndUtc &&
                b.SlotEndUtc > booking.SlotEndUtc &&
                IsActiveBookingStatus(b.Status))
            .ToListAsync();

        foreach (var affectedBooking in affectedBookings)
        {
            AddNotification(
                affectedBooking.StudentId,
                "Your mentor's session may be delayed",
                $"Your booking '{affectedBooking.DoubtTitle}' may be affected because an earlier session is running late. Estimated finish: {estimatedEndUtc:dd MMM yyyy, hh:mm tt} UTC.",
                $"/bookings/{affectedBooking.Id}");
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Running-late status updated successfully.",
            booking.Id,
            booking.EstimatedEndUtc,
            affectedStudentsNotified = affectedBookings.Count
        });
    }

    [HttpPut("{id:guid}/complete")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> CompleteSession(Guid id)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var booking = await _context.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.MentorId == mentorId);

        if (booking == null)
            return NotFound(new { message = "Booking not found." });

        if (booking.Status != BookingStatus.Ongoing)
            return Conflict(new { message = "Only an ongoing session can be completed." });

        if (!booking.ActualStartUtc.HasValue)
            return Conflict(new { message = "Start the session before completing it." });

        if (await _context.SessionRecords.AnyAsync(s => s.BookingId == booking.Id))
            return Conflict(new { message = "Session history already exists for this booking." });

        var now = DateTime.UtcNow;

        if (now <= booking.ActualStartUtc.Value)
            return Conflict(new { message = "Completion must be later than the start time." });

        booking.ActualEndUtc = now;
        booking.Status = BookingStatus.Completed;
        booking.UpdatedAtUtc = now;

        _context.SessionRecords.Add(new SessionRecord
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            StudentId = booking.StudentId,
            MentorId = booking.MentorId,
            DoubtTitle = booking.DoubtTitle,
            ScheduledStartUtc = booking.SlotStartUtc,
            ActualStartUtc = booking.ActualStartUtc.Value,
            ActualEndUtc = now,
            RecordedAtUtc = now
        });

        var mentorProfile = await _context.MentorProfiles
            .FirstOrDefaultAsync(p => p.UserId == mentorId);

        if (mentorProfile != null)
            mentorProfile.TotalDoubtsCleared++;

        var studentProfile = await _context.StudentProfiles
            .FirstOrDefaultAsync(p => p.UserId == booking.StudentId);

        if (studentProfile != null)
            studentProfile.TotalSessionsAttended++;

        AddNotification(
            booking.StudentId,
            "Session completed",
            $"Your session '{booking.DoubtTitle}' has been marked complete. You can now leave a review for your mentor.",
            $"/bookings/{booking.Id}");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                message = "The session could not be completed. Refresh the booking and check whether it has already been completed."
            });
        }

        return Ok(new
        {
            message = "Session completed and history recorded.",
            booking.Id,
            booking.Status,
            booking.ActualStartUtc,
            booking.ActualEndUtc
        });
    }

    private async Task ReleaseMatchingSlotIfAvailable(Booking booking)
    {
        var slot = await _context.AvailabilitySlots.FirstOrDefaultAsync(s =>
            s.MentorId == booking.MentorId &&
            s.StartTimeUtc == booking.SlotStartUtc &&
            s.EndTimeUtc == booking.SlotEndUtc);

        if (slot == null)
            return;

        var otherActiveBookingExists = await _context.Bookings.AnyAsync(b =>
            b.Id != booking.Id &&
            b.MentorId == booking.MentorId &&
            b.SlotStartUtc < slot.EndTimeUtc &&
            b.SlotEndUtc > slot.StartTimeUtc &&
            IsActiveBookingStatus(b.Status));

        if (!otherActiveBookingExists)
            slot.Status = AvailabilityStatus.Free;
    }

    private void AddNotification(Guid userId, string title, string message, string? link)
    {
        _context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            Link = link,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static bool IsActiveBookingStatus(BookingStatus status)
    {
        return status == BookingStatus.Pending ||
               status == BookingStatus.Approved ||
               status == BookingStatus.Ongoing ||
               status == BookingStatus.Rescheduled ||
               status == BookingStatus.NeedsReschedule;
    }

    private static object ToBookingResponse(Booking booking)
    {
        return new
        {
            booking.Id,
            booking.StudentId,
            booking.MentorId,
            booking.SlotStartUtc,
            booking.SlotEndUtc,
            booking.Mode,
            booking.DoubtTitle,
            booking.DoubtDescription,
            booking.Status,
            booking.MeetingLink,
            booking.RejectionReason,
            booking.ActualStartUtc,
            booking.ActualEndUtc,
            booking.EstimatedEndUtc,
            booking.ShiftedFromBookingId,
            booking.CreatedAtUtc
        };
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

public sealed class RequestRescheduleRequest
{
    public string? Reason { get; set; }
}

public sealed class RescheduleBookingRequest
{
    public DateTime NewSlotStartUtc { get; set; }
    public DateTime NewSlotEndUtc { get; set; }
    public SessionMode? Mode { get; set; }
}

public sealed class ReportRunningLateRequest
{
    public DateTime EstimatedEndUtc { get; set; }
}

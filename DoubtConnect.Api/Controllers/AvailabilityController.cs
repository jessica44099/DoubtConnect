
using System.Security.Claims;
using DoubtConnect.Domain.Entities;
using DoubtConnect.Domain.Enums;
using DoubtConnect.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoubtConnect.Api.Controllers;

[ApiController]
[Route("api/availability")]
public class AvailabilityController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AvailabilityController(ApplicationDbContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableSlots()
    {
        var now = DateTime.UtcNow;

        var slots = await _context.AvailabilitySlots
            .AsNoTracking()
            .Where(s =>
                s.Status == AvailabilityStatus.Free &&
                s.StartTimeUtc > now &&
                s.Mentor.Role == UserRole.Mentor &&
                !s.Mentor.IsDeleted &&
                !_context.Bookings.Any(b =>
                    b.MentorId == s.MentorId &&
                    b.SlotStartUtc < s.EndTimeUtc &&
                    b.SlotEndUtc > s.StartTimeUtc &&
                    (b.Status == BookingStatus.Pending ||
                     b.Status == BookingStatus.Approved ||
                     b.Status == BookingStatus.Ongoing ||
                     b.Status == BookingStatus.Rescheduled ||
                     b.Status == BookingStatus.NeedsReschedule)))
            .OrderBy(s => s.StartTimeUtc)
            .Select(s => new
            {
                s.Id,
                s.MentorId,
                MentorName = s.Mentor.FullName,
                Expertise = s.Mentor.MentorProfile != null
                    ? s.Mentor.MentorProfile.Expertise
                    : null,
                HourlyRate = s.Mentor.MentorProfile != null
                    ? s.Mentor.MentorProfile.HourlyRate
                    : null,
                s.StartTimeUtc,
                s.EndTimeUtc,
                s.Status
            })
            .ToListAsync();

        return Ok(slots);
    }

    [Authorize(Roles = "Mentor")]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMySlots()
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
            return Unauthorized(new { message = "This mentor account is not active." });

        var slots = await _context.AvailabilitySlots
            .AsNoTracking()
            .Where(s => s.MentorId == mentorId)
            .OrderBy(s => s.StartTimeUtc)
            .ToListAsync();

        return Ok(slots);
    }

    [Authorize(Roles = "Mentor")]
    [HttpPost]
    public async Task<IActionResult> CreateSlot(
        [FromBody] CreateAvailabilitySlotRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
            return Unauthorized(new { message = "This mentor account is not active." });

        if (!HasUtcTimes(request.StartTimeUtc, request.EndTimeUtc))
            return BadRequest(new
            {
                message = "Send both times in UTC ISO 8601 format, ending in Z."
            });

        var now = DateTime.UtcNow;

        if (request.StartTimeUtc <= now ||
            request.EndTimeUtc <= request.StartTimeUtc)
        {
            return BadRequest(new
            {
                message = "Provide a future start time and an end time after it."
            });
        }

        if ((request.EndTimeUtc - request.StartTimeUtc).TotalHours > 12)
        {
            return BadRequest(new
            {
                message = "An availability slot cannot exceed 12 hours."
            });
        }

        var overlaps = await _context.AvailabilitySlots.AnyAsync(s =>
            s.MentorId == mentorId &&
            s.StartTimeUtc < request.EndTimeUtc &&
            s.EndTimeUtc > request.StartTimeUtc);

        if (overlaps)
            return Conflict(new { message = "This time overlaps an existing availability slot." });

        var bookingConflict = await HasActiveBooking(
            mentorId, request.StartTimeUtc, request.EndTimeUtc);

        if (bookingConflict)
            return Conflict(new { message = "This time overlaps an active booking." });

        var slot = new AvailabilitySlot
        {
            Id = Guid.NewGuid(),
            MentorId = mentorId,
            StartTimeUtc = request.StartTimeUtc,
            EndTimeUtc = request.EndTimeUtc,
            Status = AvailabilityStatus.Free,
            CreatedAtUtc = now
        };

        _context.AvailabilitySlots.Add(slot);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMySlots), new { }, slot);
    }

    [Authorize(Roles = "Mentor")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateSlot(
        Guid id,
        [FromBody] UpdateAvailabilitySlotRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
            return Unauthorized(new { message = "This mentor account is not active." });

        if (!Enum.IsDefined(typeof(AvailabilityStatus), request.Status))
            return BadRequest(new { message = "Invalid availability status." });

        if (!HasUtcTimes(request.StartTimeUtc, request.EndTimeUtc))
            return BadRequest(new
            {
                message = "Send both times in UTC ISO 8601 format, ending in Z."
            });

        if (request.StartTimeUtc <= DateTime.UtcNow ||
            request.EndTimeUtc <= request.StartTimeUtc)
        {
            return BadRequest(new
            {
                message = "Provide a future start time and an end time after it."
            });
        }

        if ((request.EndTimeUtc - request.StartTimeUtc).TotalHours > 12)
        {
            return BadRequest(new
            {
                message = "An availability slot cannot exceed 12 hours."
            });
        }

        var slot = await _context.AvailabilitySlots.FirstOrDefaultAsync(s =>
            s.Id == id && s.MentorId == mentorId);

        if (slot == null)
            return NotFound(new { message = "Availability slot not found." });

        var timesChanged =
            slot.StartTimeUtc != request.StartTimeUtc ||
            slot.EndTimeUtc != request.EndTimeUtc;

        if (timesChanged && await HasActiveBooking(
                mentorId, slot.StartTimeUtc, slot.EndTimeUtc))
        {
            return Conflict(new
            {
                message = "This slot has an active booking. Its time cannot be changed."
            });
        }

        if (timesChanged)
        {
            var overlaps = await _context.AvailabilitySlots.AnyAsync(s =>
                s.MentorId == mentorId &&
                s.Id != id &&
                s.StartTimeUtc < request.EndTimeUtc &&
                s.EndTimeUtc > request.StartTimeUtc);

            if (overlaps)
                return Conflict(new { message = "The new time overlaps another availability slot." });
        }

        var bookingConflict = await HasActiveBooking(
            mentorId, request.StartTimeUtc, request.EndTimeUtc);

        if (request.Status == AvailabilityStatus.Free && bookingConflict)
        {
            return Conflict(new
            {
                message = "A slot overlapping an active booking cannot be marked free."
            });
        }

        slot.StartTimeUtc = request.StartTimeUtc;
        slot.EndTimeUtc = request.EndTimeUtc;
        slot.Status = request.Status;

        await _context.SaveChangesAsync();

        return Ok(slot);
    }

    [Authorize(Roles = "Mentor")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSlot(Guid id)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
            return Unauthorized(new { message = "This mentor account is not active." });

        var slot = await _context.AvailabilitySlots.FirstOrDefaultAsync(s =>
            s.Id == id && s.MentorId == mentorId);

        if (slot == null)
            return NotFound(new { message = "Availability slot not found." });

        if (await HasActiveBooking(
                mentorId, slot.StartTimeUtc, slot.EndTimeUtc))
        {
            return Conflict(new
            {
                message = "This slot has an active booking and cannot be deleted."
            });
        }

        _context.AvailabilitySlots.Remove(slot);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<bool> IsActiveMentor(Guid mentorId)
    {
        return await _context.Users.AnyAsync(u =>
            u.Id == mentorId &&
            u.Role == UserRole.Mentor &&
            !u.IsDeleted);
    }

    private async Task<bool> HasActiveBooking(
        Guid mentorId,
        DateTime startUtc,
        DateTime endUtc)
    {
        return await _context.Bookings.AnyAsync(b =>
            b.MentorId == mentorId &&
            b.SlotStartUtc < endUtc &&
            b.SlotEndUtc > startUtc &&
            (b.Status == BookingStatus.Pending ||
             b.Status == BookingStatus.Approved ||
             b.Status == BookingStatus.Ongoing ||
             b.Status == BookingStatus.Rescheduled ||
             b.Status == BookingStatus.NeedsReschedule));
    }

    private static bool HasUtcTimes(DateTime start, DateTime end)
    {
        return start.Kind == DateTimeKind.Utc &&
               end.Kind == DateTimeKind.Utc;
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }
}

public sealed class CreateAvailabilitySlotRequest
{
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
}

public sealed class UpdateAvailabilitySlotRequest
{
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public AvailabilityStatus Status { get; set; }
}

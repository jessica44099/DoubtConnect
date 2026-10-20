
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

    // GET: /api/availability/available
    // Anyone can browse future free slots.
    [AllowAnonymous]
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableSlots()
    {
        var now = DateTime.UtcNow;

        var slots = await _context.AvailabilitySlots
            .AsNoTracking()
            .Where(s =>
                s.Status == AvailabilityStatus.Free &&
                s.StartTimeUtc > now)
            .Include(s => s.Mentor)
                .ThenInclude(m => m.MentorProfile)
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

    // GET: /api/availability/mine
    // A mentor can see only their own slots.
    [Authorize(Roles = "Mentor")]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMySlots()
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var slots = await _context.AvailabilitySlots
            .AsNoTracking()
            .Where(s => s.MentorId == mentorId)
            .OrderBy(s => s.StartTimeUtc)
            .ToListAsync();

        return Ok(slots);
    }

    // POST: /api/availability
    [Authorize(Roles = "Mentor")]
    [HttpPost]
    public async Task<IActionResult> CreateSlot(
        CreateAvailabilitySlotRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        if (request.StartTimeUtc.Kind != DateTimeKind.Utc ||
            request.EndTimeUtc.Kind != DateTimeKind.Utc)
        {
            return BadRequest(new
            {
                message = "Send both times in UTC ISO 8601 format, ending in Z."
            });
        }

        if (request.StartTimeUtc <= DateTime.UtcNow)
        {
            return BadRequest(new
            {
                message = "The start time must be in the future."
            });
        }

        if (request.EndTimeUtc <= request.StartTimeUtc)
        {
            return BadRequest(new
            {
                message = "The end time must be after the start time."
            });
        }

        // Prevent overlapping slots for this mentor.
        var overlaps = await _context.AvailabilitySlots.AnyAsync(s =>
            s.MentorId == mentorId &&
            s.StartTimeUtc < request.EndTimeUtc &&
            s.EndTimeUtc > request.StartTimeUtc);

        if (overlaps)
        {
            return Conflict(new
            {
                message = "This time overlaps an existing availability slot."
            });
        }

        var slot = new AvailabilitySlot
        {
            Id = Guid.NewGuid(),
            MentorId = mentorId,
            StartTimeUtc = request.StartTimeUtc,
            EndTimeUtc = request.EndTimeUtc,
            Status = AvailabilityStatus.Free,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.AvailabilitySlots.Add(slot);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetMySlots),
            new { },
            slot);
    }

    // PUT: /api/availability/{id}
    [Authorize(Roles = "Mentor")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateSlot(
        Guid id,
        UpdateAvailabilitySlotRequest request)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        if (!Enum.IsDefined(typeof(AvailabilityStatus), request.Status))
        {
            return BadRequest(new { message = "Invalid availability status." });
        }

        if (request.StartTimeUtc.Kind != DateTimeKind.Utc ||
            request.EndTimeUtc.Kind != DateTimeKind.Utc)
        {
            return BadRequest(new
            {
                message = "Send both times in UTC ISO 8601 format, ending in Z."
            });
        }

        if (request.StartTimeUtc <= DateTime.UtcNow ||
            request.EndTimeUtc <= request.StartTimeUtc)
        {
            return BadRequest(new
            {
                message = "Provide a future start time and an end time after it."
            });
        }

        var slot = await _context.AvailabilitySlots
            .FirstOrDefaultAsync(s =>
                s.Id == id && s.MentorId == mentorId);

        if (slot == null)
            return NotFound(new { message = "Availability slot not found." });

        var hasActiveBooking = await HasActiveBooking(
            mentorId, slot.StartTimeUtc, slot.EndTimeUtc);

        var timesChanged =
            slot.StartTimeUtc != request.StartTimeUtc ||
            slot.EndTimeUtc != request.EndTimeUtc;

        if (timesChanged)
        {
            if (hasActiveBooking)
            {
                return Conflict(new
                {
                    message = "This slot has an active booking. Its time cannot be changed."
                });
            }

            var overlaps = await _context.AvailabilitySlots.AnyAsync(s =>
                s.MentorId == mentorId &&
                s.Id != id &&
                s.StartTimeUtc < request.EndTimeUtc &&
                s.EndTimeUtc > request.StartTimeUtc);

            if (overlaps)
            {
                return Conflict(new
                {
                    message = "The new time overlaps another availability slot."
                });
            }
        }

        if (request.Status == AvailabilityStatus.Free && hasActiveBooking)
        {
            return Conflict(new
            {
                message = "A slot with an active booking cannot be marked free."
            });
        }

        slot.StartTimeUtc = request.StartTimeUtc;
        slot.EndTimeUtc = request.EndTimeUtc;
        slot.Status = request.Status;

        await _context.SaveChangesAsync();

        return Ok(slot);
    }

    // DELETE: /api/availability/{id}
    [Authorize(Roles = "Mentor")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSlot(Guid id)
    {
        if (!TryGetUserId(out var mentorId))
            return Unauthorized();

        var slot = await _context.AvailabilitySlots
            .FirstOrDefaultAsync(s =>
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


using System.Globalization;
using System.Security.Claims;
using DoubtConnect.Domain.Entities;
using DoubtConnect.Domain.Enums;
using DoubtConnect.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoubtConnect.Api.Controllers;

[ApiController]
[Route("api/mentor-calendar-events")]
[Authorize(Roles = "Mentor")]
public class MentorCalendarEventsController : ControllerBase
{
    private static readonly string[] AllowedActivityTypes =
    {
        "Doubt Session",
        "Meeting",
        "Class",
        "Break",
        "Personal",
        "Leave",
        "Other"
    };

    private readonly ApplicationDbContext _context;

    public MentorCalendarEventsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetEvents(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        if (!TryGetMentorId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
            return Unauthorized(new { message = "This mentor account is not active." });

        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            return BadRequest(new
            {
                message = "'from' must be on or before 'to'."
            });
        }

        var query = _context.MentorCalendarEvents
            .AsNoTracking()
            .Where(e => e.MentorId == mentorId);

        if (from.HasValue)
            query = query.Where(e => e.EventDate >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.EventDate <= to.Value);

        var records = await query
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.StartTime)
            .ToListAsync();

        return Ok(records.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent(
        [FromBody] MentorCalendarEventRequest request)
    {
        if (!TryGetMentorId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
            return Unauthorized(new { message = "This mentor account is not active." });

        var validationError = ValidateRequest(
            request,
            out var date,
            out var start,
            out var end,
            out var activityType,
            out var title,
            out var studentName);

        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var overlaps = await _context.MentorCalendarEvents.AnyAsync(e =>
            e.MentorId == mentorId &&
            e.EventDate == date &&
            start < e.EndTime &&
            end > e.StartTime);

        if (overlaps)
        {
            return Conflict(new
            {
                message = "This event overlaps another manual calendar event."
            });
        }

        var (eventStartUtc, eventEndUtc) = ConvertToUtc(date, start, end);

        if (await HasBookingConflict(mentorId, eventStartUtc, eventEndUtc))
        {
            return Conflict(new
            {
                message = "This event overlaps an active student booking. Review the session before blocking this time."
            });
        }

        var calendarEvent = new MentorCalendarEvent
        {
            MentorId = mentorId,
            EventDate = date,
            StartTime = start,
            EndTime = end,
            ActivityType = activityType,
            Title = title,
            StudentName = studentName,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.MentorCalendarEvents.Add(calendarEvent);
        await _context.SaveChangesAsync();

        return Ok(ToResponse(calendarEvent));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateEvent(
        Guid id,
        [FromBody] MentorCalendarEventRequest request)
    {
        if (!TryGetMentorId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
            return Unauthorized(new { message = "This mentor account is not active." });

        var calendarEvent = await _context.MentorCalendarEvents
            .FirstOrDefaultAsync(e =>
                e.Id == id && e.MentorId == mentorId);

        if (calendarEvent is null)
        {
            return NotFound(new { message = "Calendar event not found." });
        }

        var validationError = ValidateRequest(
            request,
            out var date,
            out var start,
            out var end,
            out var activityType,
            out var title,
            out var studentName);

        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var overlaps = await _context.MentorCalendarEvents.AnyAsync(e =>
            e.Id != id &&
            e.MentorId == mentorId &&
            e.EventDate == date &&
            start < e.EndTime &&
            end > e.StartTime);

        if (overlaps)
        {
            return Conflict(new
            {
                message = "This event overlaps another manual calendar event."
            });
        }

        var (eventStartUtc, eventEndUtc) = ConvertToUtc(date, start, end);

        if (await HasBookingConflict(mentorId, eventStartUtc, eventEndUtc))
        {
            return Conflict(new
            {
                message = "This event overlaps an active student booking. Review the session before blocking this time."
            });
        }

        calendarEvent.EventDate = date;
        calendarEvent.StartTime = start;
        calendarEvent.EndTime = end;
        calendarEvent.ActivityType = activityType;
        calendarEvent.Title = title;
        calendarEvent.StudentName = studentName;
        calendarEvent.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(ToResponse(calendarEvent));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid id)
    {
        if (!TryGetMentorId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
            return Unauthorized(new { message = "This mentor account is not active." });

        var calendarEvent = await _context.MentorCalendarEvents
            .FirstOrDefaultAsync(e =>
                e.Id == id && e.MentorId == mentorId);

        if (calendarEvent is null)
        {
            return NotFound(new { message = "Calendar event not found." });
        }

        _context.MentorCalendarEvents.Remove(calendarEvent);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private static string? ValidateRequest(
        MentorCalendarEventRequest request,
        out DateOnly date,
        out TimeOnly start,
        out TimeOnly end,
        out string activityType,
        out string title,
        out string? studentName)
    {
        date = default;
        start = default;
        end = default;
        activityType = string.Empty;
        title = string.Empty;
        studentName = null;

        if (request is null)
            return "Event details are required.";

        if (!DateOnly.TryParseExact(
                request.Date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            return "Enter a valid date in yyyy-MM-dd format.";
        }

        if (!TimeOnly.TryParseExact(
                request.StartTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out start) ||
            !TimeOnly.TryParseExact(
                request.EndTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out end))
        {
            return "Enter start and end times in HH:mm format.";
        }

        if (start >= end)
            return "End time must be after start time.";

        if (start < new TimeOnly(10, 0) ||
            end > new TimeOnly(20, 0))
        {
            return "Events must be within 10 AM–8 PM India time.";
        }

        activityType = request.ActivityType?.Trim() ?? string.Empty;

        if (!AllowedActivityTypes.Contains(activityType))
            return "Select a valid activity type.";

        title = request.Title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return "Enter an event title (up to 200 characters).";

        if (activityType == "Doubt Session")
        {
            studentName = request.StudentName?.Trim();

            if (string.IsNullOrWhiteSpace(studentName) ||
                studentName.Length > 150)
            {
                return "Enter the student name for a doubt session.";
            }
        }

        return null;
    }

    private async Task<bool> HasBookingConflict(
        Guid mentorId,
        DateTime eventStartUtc,
        DateTime eventEndUtc)
    {
        return await _context.Bookings.AnyAsync(b =>
            b.MentorId == mentorId &&
            b.SlotStartUtc < eventEndUtc &&
            b.SlotEndUtc > eventStartUtc &&
            (b.Status == BookingStatus.Pending ||
             b.Status == BookingStatus.Approved ||
             b.Status == BookingStatus.Ongoing ||
             b.Status == BookingStatus.Rescheduled ||
             b.Status == BookingStatus.NeedsReschedule));
    }

    private async Task<bool> IsActiveMentor(Guid mentorId)
    {
        return await _context.Users.AnyAsync(u =>
            u.Id == mentorId &&
            u.Role == UserRole.Mentor &&
            !u.IsDeleted);
    }

    private static (DateTime StartUtc, DateTime EndUtc) ConvertToUtc(
        DateOnly date,
        TimeOnly start,
        TimeOnly end)
    {
        var indiaTimeZone = GetIndiaTimeZone();

        var localStart = DateTime.SpecifyKind(
            date.ToDateTime(start),
            DateTimeKind.Unspecified);

        var localEnd = DateTime.SpecifyKind(
            date.ToDateTime(end),
            DateTimeKind.Unspecified);

        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, indiaTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, indiaTimeZone)
        );
    }

    private static TimeZoneInfo GetIndiaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
    }

    private static object ToResponse(MentorCalendarEvent calendarEvent)
    {
        return new
        {
            id = calendarEvent.Id,
            date = calendarEvent.EventDate.ToString("yyyy-MM-dd"),
            startTime = calendarEvent.StartTime.ToString("HH:mm"),
            endTime = calendarEvent.EndTime.ToString("HH:mm"),
            activityType = calendarEvent.ActivityType,
            title = calendarEvent.Title,
            studentName = calendarEvent.StudentName,
            updatedAtUtc = calendarEvent.UpdatedAtUtc
        };
    }

    private bool TryGetMentorId(out Guid mentorId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out mentorId);
    }
}

public sealed class MentorCalendarEventRequest
{
    public string Date { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string ActivityType { get; set; } = "Meeting";
    public string Title { get; set; } = string.Empty;
    public string? StudentName { get; set; }
}

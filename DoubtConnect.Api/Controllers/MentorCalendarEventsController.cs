
using System.Security.Claims;
using DoubtConnect.Domain.Entities;
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

        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return BadRequest(new { message = "'from' must be on or before 'to'." });

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

        var events = records.Select(e => new
        {
            id = e.Id,
            date = e.EventDate.ToString("yyyy-MM-dd"),
            startTime = e.StartTime.ToString("HH:mm"),
            endTime = e.EndTime.ToString("HH:mm"),
            activityType = e.ActivityType,
            title = e.Title,
            studentName = e.StudentName
        });

        return Ok(events);
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent(
        [FromBody] MentorCalendarEventRequest request)
    {
        if (!TryGetMentorId(out var mentorId))
            return Unauthorized();

        if (!DateOnly.TryParse(request.Date, out var date))
            return BadRequest(new { message = "Enter a valid event date." });

        if (!TimeOnly.TryParse(request.StartTime, out var start) ||
            !TimeOnly.TryParse(request.EndTime, out var end))
            return BadRequest(new { message = "Enter valid start and end times." });

        if (start >= end)
            return BadRequest(new { message = "End time must be after start time." });

        if (start < new TimeOnly(10, 0) || end > new TimeOnly(20, 0))
            return BadRequest(new { message = "Events must be within 10 AM–8 PM." });

        var allowedTypes = new[]
        {
            "Doubt Session", "Meeting", "Class", "Break",
            "Personal", "Leave", "Other"
        };

        if (string.IsNullOrWhiteSpace(request.ActivityType) ||
            !allowedTypes.Contains(request.ActivityType.Trim()))
            return BadRequest(new { message = "Select a valid activity type." });

        if (string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 200)
            return BadRequest(new { message = "Enter an event title (up to 200 characters)." });

        var isDoubtSession = request.ActivityType.Trim() == "Doubt Session";

        if (isDoubtSession &&
            (string.IsNullOrWhiteSpace(request.StudentName) ||
             request.StudentName.Trim().Length > 150))
            return BadRequest(new { message = "Enter the student name for a doubt session." });

        var overlaps = await _context.MentorCalendarEvents.AnyAsync(e =>
            e.MentorId == mentorId &&
            e.EventDate == date &&
            start < e.EndTime &&
            end > e.StartTime);

        if (overlaps)
            return Conflict(new { message = "This event overlaps another manual calendar event." });

        var calendarEvent = new MentorCalendarEvent
        {
            MentorId = mentorId,
            EventDate = date,
            StartTime = start,
            EndTime = end,
            ActivityType = request.ActivityType.Trim(),
            Title = request.Title.Trim(),
            StudentName = isDoubtSession ? request.StudentName!.Trim() : null,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.MentorCalendarEvents.Add(calendarEvent);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = calendarEvent.Id,
            date = date.ToString("yyyy-MM-dd"),
            startTime = start.ToString("HH:mm"),
            endTime = end.ToString("HH:mm"),
            activityType = calendarEvent.ActivityType,
            title = calendarEvent.Title,
            studentName = calendarEvent.StudentName
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid id)
    {
        if (!TryGetMentorId(out var mentorId))
            return Unauthorized();

        var calendarEvent = await _context.MentorCalendarEvents
            .FirstOrDefaultAsync(e => e.Id == id && e.MentorId == mentorId);

        if (calendarEvent is null)
            return NotFound(new { message = "Calendar event not found." });

        _context.MentorCalendarEvents.Remove(calendarEvent);
        await _context.SaveChangesAsync();

        return NoContent();
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

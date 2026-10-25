
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
[Route("api/weekly-availability")]
[Authorize(Roles = "Mentor")]
public class WeeklyAvailabilityController : ControllerBase
{
    private const int MaxScheduleBlocks = 100;
    private const int MaxActivityTypeLength = 50;
    private const int MaxTitleLength = 200;

    private readonly ApplicationDbContext _context;

    public WeeklyAvailabilityController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetWeeklyAvailability()
    {
        if (!TryGetMentorId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
        {
            return Unauthorized(new
            {
                message = "This mentor account is not active."
            });
        }

        var saved = await _context.WeeklyAvailabilityTemplates
            .AsNoTracking()
            .Where(t => t.MentorId == mentorId)
            .OrderBy(t => t.DayOfWeek)
            .ThenBy(t => t.StartTime)
            .Select(t => new
            {
                dayOfWeek = t.DayOfWeek,
                startTime = t.StartTime.ToString("HH:mm"),
                endTime = t.EndTime.ToString("HH:mm"),
                isFree = t.IsFree,
                activityType = t.ActivityType,
                title = t.Title
            })
            .ToListAsync();

        if (saved.Count > 0)
            return Ok(saved);

        // Default timetable: 10:00–20:00 every day.
        return Ok(Enum.GetValues<DayOfWeek>().Select(day => new
        {
            dayOfWeek = day,
            startTime = "10:00",
            endTime = "20:00",
            isFree = true,
            activityType = "Free",
            title = (string?)null
        }));
    }

    [HttpPut]
    public async Task<IActionResult> SaveWeeklyAvailability(
        [FromBody] List<WeeklyAvailabilityRequest> request)
    {
        if (!TryGetMentorId(out var mentorId))
            return Unauthorized();

        if (!await IsActiveMentor(mentorId))
        {
            return Unauthorized(new
            {
                message = "This mentor account is not active."
            });
        }

        if (request is null || request.Count == 0)
        {
            return BadRequest(new
            {
                message = "Provide at least one schedule block."
            });
        }

        if (request.Count > MaxScheduleBlocks)
        {
            return BadRequest(new
            {
                message = $"A maximum of {MaxScheduleBlocks} schedule blocks is allowed."
            });
        }

        // Validate every block before changing any saved data.
        foreach (var item in request)
        {
            if (!Enum.IsDefined(typeof(DayOfWeek), item.DayOfWeek))
            {
                return BadRequest(new
                {
                    message = "Each block must have a valid day of the week."
                });
            }

            if (!TimeOnly.TryParseExact(
                    item.StartTime,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var start) ||
                !TimeOnly.TryParseExact(
                    item.EndTime,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var end))
            {
                return BadRequest(new
                {
                    message = "Times must use HH:mm format, for example 10:00 or 20:00."
                });
            }

            if (start >= end)
            {
                return BadRequest(new
                {
                    message = "Each block's end time must be after its start time."
                });
            }

            if (string.IsNullOrWhiteSpace(item.ActivityType) ||
                item.ActivityType.Trim().Length > MaxActivityTypeLength)
            {
                return BadRequest(new
                {
                    message = $"ActivityType is required and cannot exceed {MaxActivityTypeLength} characters."
                });
            }

            if (item.Title?.Length > MaxTitleLength)
            {
                return BadRequest(new
                {
                    message = $"Title cannot exceed {MaxTitleLength} characters."
                });
            }
        }

        // Reject overlaps within each day.
        foreach (var dayGroup in request.GroupBy(x => x.DayOfWeek))
        {
            var ordered = dayGroup
                .Select(x => new
                {
                    Start = TimeOnly.ParseExact(
                        x.StartTime,
                        "HH:mm",
                        CultureInfo.InvariantCulture),
                    End = TimeOnly.ParseExact(
                        x.EndTime,
                        "HH:mm",
                        CultureInfo.InvariantCulture)
                })
                .OrderBy(x => x.Start)
                .ToList();

            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Start < ordered[i - 1].End)
                {
                    return BadRequest(new
                    {
                        message = $"Schedule blocks overlap on {dayGroup.Key}."
                    });
                }
            }
        }

        // Replace the timetable atomically so a failed save cannot leave
        // the mentor with a partially replaced schedule.
        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        var existing = await _context.WeeklyAvailabilityTemplates
            .Where(t => t.MentorId == mentorId)
            .ToListAsync();

        _context.WeeklyAvailabilityTemplates.RemoveRange(existing);

        foreach (var item in request)
        {
            _context.WeeklyAvailabilityTemplates.Add(
                new WeeklyAvailabilityTemplate
                {
                    Id = Guid.NewGuid(),
                    MentorId = mentorId,
                    DayOfWeek = item.DayOfWeek,
                    StartTime = TimeOnly.ParseExact(
                        item.StartTime,
                        "HH:mm",
                        CultureInfo.InvariantCulture),
                    EndTime = TimeOnly.ParseExact(
                        item.EndTime,
                        "HH:mm",
                        CultureInfo.InvariantCulture),
                    IsFree = item.IsFree,
                    ActivityType = item.ActivityType.Trim(),
                    Title = string.IsNullOrWhiteSpace(item.Title)
                        ? null
                        : item.Title.Trim()
                });
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            message = "Weekly timetable saved successfully.",
            blocksSaved = request.Count
        });
    }

    private async Task<bool> IsActiveMentor(Guid mentorId)
    {
        return await _context.Users.AnyAsync(u =>
            u.Id == mentorId &&
            u.Role == UserRole.Mentor &&
            !u.IsDeleted);
    }

    private bool TryGetMentorId(out Guid mentorId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out mentorId);
    }
}

public sealed class WeeklyAvailabilityRequest
{
    public DayOfWeek DayOfWeek { get; set; }

    public string StartTime { get; set; } = "10:00";

    public string EndTime { get; set; } = "20:00";

    public bool IsFree { get; set; } = true;

    public string ActivityType { get; set; } = "Free";

    public string? Title { get; set; }
}

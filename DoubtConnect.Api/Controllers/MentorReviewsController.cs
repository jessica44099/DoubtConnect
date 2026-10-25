
using System.Security.Claims;
using DoubtConnect.Domain.Entities;
using DoubtConnect.Domain.Enums;
using DoubtConnect.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoubtConnect.Api.Controllers;

[ApiController]
[Route("api/mentor-reviews")]
[Authorize]
public class MentorReviewsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public MentorReviewsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost("bookings/{bookingId:guid}")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> CreateReview(
        Guid bookingId,
        [FromBody] CreateMentorReviewRequest request)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized();

        var studentIsActive = await _context.Users
            .AnyAsync(u => u.Id == studentId && !u.IsDeleted);

        if (!studentIsActive)
            return Unauthorized(new { message = "This account is not active." });

        if (request.Rating < 1 || request.Rating > 5)
        {
            return BadRequest(new
            {
                message = "Rating must be between 1 and 5."
            });
        }

        if (request.Comment?.Length > 500)
        {
            return BadRequest(new
            {
                message = "Comment must not exceed 500 characters."
            });
        }

        var booking = await _context.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(b =>
                b.Id == bookingId &&
                b.StudentId == studentId);

        if (booking == null)
        {
            return NotFound(new { message = "Booking not found." });
        }

        if (booking.Status != BookingStatus.Completed)
        {
            return BadRequest(new
            {
                message = "You can review only completed sessions."
            });
        }

        var mentorIsActive = await _context.Users
            .AnyAsync(u =>
                u.Id == booking.MentorId &&
                u.Role == UserRole.Mentor &&
                !u.IsDeleted);

        if (!mentorIsActive)
        {
            return BadRequest(new
            {
                message = "This mentor account is no longer active."
            });
        }

        var alreadyReviewed = await _context.MentorReviews
            .AnyAsync(r => r.BookingId == bookingId);

        if (alreadyReviewed)
        {
            return Conflict(new
            {
                message = "You have already reviewed this session."
            });
        }

        var review = new MentorReview
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            StudentId = studentId,
            MentorId = booking.MentorId,
            Rating = request.Rating,
            Comment = string.IsNullOrWhiteSpace(request.Comment)
                ? null
                : request.Comment.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.MentorReviews.Add(review);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique BookingId index protects against concurrent duplicates.
            return Conflict(new
            {
                message = "A review already exists for this session, or the review could not be saved."
            });
        }

        return Created(
            $"/api/mentor-reviews/mentors/{review.MentorId}",
            new
            {
                review.Id,
                review.BookingId,
                review.MentorId,
                review.Rating,
                review.Comment,
                review.CreatedAtUtc
            });
    }

    [HttpGet("mentors/{mentorId:guid}")]
    public async Task<IActionResult> GetMentorReviews(Guid mentorId)
    {
        var mentorExists = await _context.Users
            .AnyAsync(u =>
                u.Id == mentorId &&
                u.Role == UserRole.Mentor &&
                !u.IsDeleted);

        if (!mentorExists)
        {
            return NotFound(new { message = "Mentor not found." });
        }

        var reviews = await _context.MentorReviews
            .AsNoTracking()
            .Where(r => r.MentorId == mentorId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.BookingId,
                r.StudentId,
                r.Rating,
                r.Comment,
                r.CreatedAtUtc
            })
            .ToListAsync();

        var studentIds = reviews
            .Select(r => r.StudentId)
            .Distinct()
            .ToList();

        var studentNames = await _context.Users
            .AsNoTracking()
            .Where(u => studentIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.IsDeleted
            })
            .ToDictionaryAsync(u => u.Id);

        var reviewItems = reviews.Select(r =>
        {
            studentNames.TryGetValue(r.StudentId, out var student);

            return new
            {
                r.Id,
                r.BookingId,
                StudentName = student == null || student.IsDeleted
                    ? "Deleted user"
                    : student.FullName,
                r.Rating,
                r.Comment,
                r.CreatedAtUtc
            };
        }).ToList();

        var averageRating = reviews.Count == 0
            ? 0
            : Math.Round(reviews.Average(r => r.Rating), 1);

        return Ok(new
        {
            MentorId = mentorId,
            AverageRating = averageRating,
            TotalReviews = reviews.Count,
            Reviews = reviewItems
        });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }
}

public sealed class CreateMentorReviewRequest
{
    public int Rating { get; set; }

    public string? Comment { get; set; }
}

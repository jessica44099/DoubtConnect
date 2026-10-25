
using System.Security.Claims;
using DoubtConnect.Domain.Entities;
using DoubtConnect.Domain.Enums;
using DoubtConnect.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoubtConnect.Api.Controllers;

[ApiController]
[Route("api/profiles")]
[Authorize]
public class ProfilesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ProfilesController(ApplicationDbContext db)
    {
        _db = db;
    }

    private bool TryGetUserId(out Guid userId)
    {
        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out userId);
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);

        if (user is null)
            return NotFound(new { message = "User not found." });

        if (user.Role == UserRole.Student)
        {
            var profile = await _db.StudentProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            return Ok(new
            {
                UserId = user.Id,
                user.FullName,
                user.Email,
                Role = "Student",
                Profile = profile is null ? null : new
                {
                    profile.Id,
                    profile.EducationLevel,
                    profile.TotalSessionsAttended
                }
            });
        }

        if (user.Role == UserRole.Mentor)
        {
            var profile = await _db.MentorProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            var statistics = await GetMentorStatistics(userId);

            var completionPercentage = CalculateProfileCompletion(profile);

            return Ok(new
            {
                UserId = user.Id,
                user.FullName,
                user.Email,
                Role = "Mentor",
                Profile = new
                {
                    profile?.Id,
                    Expertise = profile?.Expertise,
                    Bio = profile?.Bio,
                    profile?.Education,
                    profile?.Certifications,
                    profile?.Achievements,
                    profile?.HourlyRate,
                    TotalDoubtsCleared = statistics.CompletedSessions,
                    ProfileCompletionPercentage = completionPercentage,
                    IsProfileComplete = completionPercentage == 100,
                    statistics.CompletedSessions,
                    statistics.DistinctStudents,
                    statistics.AverageRating,
                    statistics.TotalReviews,
                    statistics.CompletedSessionHours
                }
            });
        }

        return BadRequest(new { message = "Unsupported user role." });
    }

    [Authorize(Roles = "Student")]
    [HttpPut("student")]
    public async Task<IActionResult> UpdateStudentProfile(
        [FromBody] StudentProfileRequest request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (request.EducationLevel?.Length > 100)
        {
            return BadRequest(new
            {
                message = "Education level cannot exceed 100 characters."
            });
        }

        var activeUserExists = await _db.Users
            .AnyAsync(u =>
                u.Id == userId &&
                u.Role == UserRole.Student &&
                !u.IsDeleted);

        if (!activeUserExists)
            return Unauthorized(new
            {
                message = "This account is not active."
            });

        var profile = await _db.StudentProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile is null)
        {
            profile = new StudentProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId
            };

            _db.StudentProfiles.Add(profile);
        }

        profile.EducationLevel = string.IsNullOrWhiteSpace(
            request.EducationLevel)
                ? null
                : request.EducationLevel.Trim();

        await _db.SaveChangesAsync();

        return Ok(new
        {
            Message = "Student profile saved successfully.",
            profile.Id,
            profile.EducationLevel,
            profile.TotalSessionsAttended
        });
    }

    [Authorize(Roles = "Mentor")]
    [HttpPut("mentor")]
    public async Task<IActionResult> UpdateMentorProfile(
        [FromBody] MentorProfileRequest request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var activeUserExists = await _db.Users
            .AnyAsync(u =>
                u.Id == userId &&
                u.Role == UserRole.Mentor &&
                !u.IsDeleted);

        if (!activeUserExists)
        {
            return Unauthorized(new
            {
                message = "This account is not active."
            });
        }

        var allowedSubjects = new HashSet<string>(
            new[] { "Quant", "VARC", "DILR" },
            StringComparer.OrdinalIgnoreCase);

        var selectedSubjects = (request.Expertise ?? string.Empty)
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedSubjects.Count == 0)
        {
            return BadRequest(new
            {
                message = "Please select at least one teaching subject."
            });
        }

        if (selectedSubjects.Any(
            subject => !allowedSubjects.Contains(subject)))
        {
            return BadRequest(new
            {
                message = "Only Quant, VARC, and DILR are supported."
            });
        }

        if (request.Bio?.Length > 2000)
        {
            return BadRequest(new
            {
                message = "Bio cannot exceed 2000 characters."
            });
        }

        if (request.Education?.Length > 200)
        {
            return BadRequest(new
            {
                message = "Education cannot exceed 200 characters."
            });
        }

        if (request.Certifications?.Length > 2000)
        {
            return BadRequest(new
            {
                message = "Certifications cannot exceed 2000 characters."
            });
        }

        if (request.Achievements?.Length > 2000)
        {
            return BadRequest(new
            {
                message = "Achievements cannot exceed 2000 characters."
            });
        }

        if (request.HourlyRate < 0)
        {
            return BadRequest(new
            {
                message = "Hourly rate cannot be negative."
            });
        }

        var normalizedSubjects = new[] { "Quant", "VARC", "DILR" }
            .Where(subject => selectedSubjects.Contains(
                subject,
                StringComparer.OrdinalIgnoreCase))
            .ToList();

        var profile = await _db.MentorProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile is null)
        {
            profile = new MentorProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId
            };

            _db.MentorProfiles.Add(profile);
        }

        profile.Expertise = string.Join(", ", normalizedSubjects);
        profile.Bio = NormalizeOptionalText(request.Bio);
        profile.Education = NormalizeOptionalText(request.Education);
        profile.Certifications = NormalizeOptionalText(
            request.Certifications);
        profile.Achievements = NormalizeOptionalText(
            request.Achievements);
        profile.HourlyRate = request.HourlyRate;

        await _db.SaveChangesAsync();

        var statistics = await GetMentorStatistics(userId);
        var completionPercentage = CalculateProfileCompletion(profile);

        return Ok(new
        {
            Message = "Mentor profile saved successfully.",
            profile.Id,
            profile.Expertise,
            profile.Bio,
            profile.Education,
            profile.Certifications,
            profile.Achievements,
            profile.HourlyRate,
            TotalDoubtsCleared = statistics.CompletedSessions,
            ProfileCompletionPercentage = completionPercentage,
            IsProfileComplete = completionPercentage == 100,
            statistics.CompletedSessions,
            statistics.DistinctStudents,
            statistics.AverageRating,
            statistics.TotalReviews,
            statistics.CompletedSessionHours
        });
    }

    [AllowAnonymous]
    [HttpGet("mentors/{userId:guid}")]
    public async Task<IActionResult> GetPublicMentorProfile(Guid userId)
    {
        var mentor = await _db.Users
            .AsNoTracking()
            .Where(u =>
                u.Id == userId &&
                u.Role == UserRole.Mentor &&
                !u.IsDeleted)
            .Select(u => new
            {
                u.Id,
                u.FullName
            })
            .FirstOrDefaultAsync();

        if (mentor is null)
        {
            return NotFound(new
            {
                message = "Mentor not found."
            });
        }

        var profile = await _db.MentorProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId);

        var statistics = await GetMentorStatistics(userId);
        var completionPercentage = CalculateProfileCompletion(profile);

        return Ok(new
        {
            UserId = mentor.Id,
            Name = mentor.FullName,
            Expertise = profile?.Expertise,
            Bio = profile?.Bio,
            Education = profile?.Education,
            Certifications = profile?.Certifications,
            Achievements = profile?.Achievements,
            HourlyRate = profile?.HourlyRate,
            ProfileCompletionPercentage = completionPercentage,
            TotalDoubtsCleared = statistics.CompletedSessions,
            statistics.CompletedSessions,
            statistics.DistinctStudents,
            statistics.AverageRating,
            statistics.TotalReviews,
            statistics.CompletedSessionHours
        });
    }

    private async Task<MentorStatistics> GetMentorStatistics(
        Guid mentorId)
    {
        var completedBookings = await _db.Bookings
            .AsNoTracking()
            .Where(b =>
                b.MentorId == mentorId &&
                b.Status == BookingStatus.Completed)
            .Select(b => new
            {
                b.Id,
                b.StudentId
            })
            .ToListAsync();

        var completedBookingIds = completedBookings
            .Select(b => b.Id)
            .ToList();

        var sessionRecords = await _db.SessionRecords
            .AsNoTracking()
            .Where(s =>
                s.MentorId == mentorId &&
                completedBookingIds.Contains(s.BookingId))
            .Select(s => new
            {
                s.ActualStartUtc,
                s.ActualEndUtc
            })
            .ToListAsync();

        var totalHours = sessionRecords
            .Where(s => s.ActualEndUtc > s.ActualStartUtc)
            .Sum(s =>
                (s.ActualEndUtc - s.ActualStartUtc).TotalHours);

        var reviewRatings = await _db.MentorReviews
            .AsNoTracking()
            .Where(r =>
                r.MentorId == mentorId &&
                completedBookingIds.Contains(r.BookingId))
            .Select(r => r.Rating)
            .ToListAsync();

        var averageRating = reviewRatings.Count == 0
            ? 0
            : Math.Round(reviewRatings.Average(), 1);

        return new MentorStatistics(
            CompletedSessions: completedBookings.Count,
            DistinctStudents: completedBookings
                .Select(b => b.StudentId)
                .Distinct()
                .Count(),
            AverageRating: averageRating,
            TotalReviews: reviewRatings.Count,
            CompletedSessionHours: Math.Round(totalHours, 2));
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static int CalculateProfileCompletion(
        MentorProfile? profile)
    {
        if (profile is null)
            return 0;

        // Six profile fields, each worth one-sixth of completion.
        var completedFields = new[]
        {
            !string.IsNullOrWhiteSpace(profile.Expertise),
            !string.IsNullOrWhiteSpace(profile.Bio),
            !string.IsNullOrWhiteSpace(profile.Education),
            !string.IsNullOrWhiteSpace(profile.Certifications),
            !string.IsNullOrWhiteSpace(profile.Achievements),
            profile.HourlyRate.HasValue
        }.Count(isComplete => isComplete);

        return (int)Math.Round(
            completedFields * 100.0 / 6,
            MidpointRounding.AwayFromZero);
    }

    private sealed record MentorStatistics(
        int CompletedSessions,
        int DistinctStudents,
        double AverageRating,
        int TotalReviews,
        double CompletedSessionHours);
}

public sealed class StudentProfileRequest
{
    public string? EducationLevel { get; set; }
}

public sealed class MentorProfileRequest
{
    public string Expertise { get; set; } = string.Empty;

    public string? Bio { get; set; }

    public string? Education { get; set; }

    public string? Certifications { get; set; }

    public string? Achievements { get; set; }

    public decimal? HourlyRate { get; set; }
}

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

    // GET api/profiles/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return NotFound(new { message = "User not found." });

        if (user.Role == UserRole.Student)
        {
            var profile = await _db.StudentProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            return Ok(new
            {
                userId = user.Id,
                user.FullName,
                user.Email,
                role = "Student",
                profile = profile is null ? null : new
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

            return Ok(new
            {
                userId = user.Id,
                user.FullName,
                user.Email,
                role = "Mentor",
                profile = profile is null ? null : new
                {
                    profile.Id,
                    profile.Expertise,
                    profile.Bio,
                    profile.TotalDoubtsCleared
                }
            });
        }

        return BadRequest(new { message = "Unsupported user role." });
    }

    // PUT api/profiles/student
    [Authorize(Roles = "Student")]
    [HttpPut("student")]
    public async Task<IActionResult> UpdateStudentProfile(
        StudentProfileRequest request)
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
            message = "Student profile saved successfully.",
            profile.Id,
            profile.EducationLevel,
            profile.TotalSessionsAttended
        });
    }

    // PUT api/profiles/mentor
    [Authorize(Roles = "Mentor")]
    [HttpPut("mentor")]
    public async Task<IActionResult> UpdateMentorProfile(
        MentorProfileRequest request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        // Only these three teaching subjects are supported.
        var allowedSubjects = new HashSet<string>(
            new[] { "Quant", "VARC", "DILR" },
            StringComparer.OrdinalIgnoreCase);

        // Accept one or multiple subjects separated by commas.
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

        // Save subjects in a consistent order.
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

        profile.Bio = string.IsNullOrWhiteSpace(request.Bio)
            ? null
            : request.Bio.Trim();

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Mentor profile saved successfully.",
            profile.Id,
            profile.Expertise,
            profile.Bio,
            profile.TotalDoubtsCleared
        });
    }

    // GET api/profiles/mentors/{userId}
    // Returns public mentor details without private or payment-related fields.
    [HttpGet("mentors/{userId:guid}")]
    public async Task<IActionResult> GetPublicMentorProfile(Guid userId)
    {
        var profile = await _db.MentorProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new
            {
                p.UserId,
                name = p.User.FullName,
                p.Expertise,
                p.Bio,
                p.TotalDoubtsCleared
            })
            .FirstOrDefaultAsync();

        if (profile is null)
        {
            return NotFound(new
            {
                message = "Mentor profile not found."
            });
        }

        return Ok(profile);
    }
}

public sealed class StudentProfileRequest
{
    public string? EducationLevel { get; set; }
}

public sealed class MentorProfileRequest
{
    public string Expertise { get; set; } = string.Empty;

    public string? Bio { get; set; }
}
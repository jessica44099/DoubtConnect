
using System.ComponentModel.DataAnnotations;

namespace DoubtConnect.Api.DTOs;

public class RegisterRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [RegularExpression(
        "^(Student|Mentor)$",
        ErrorMessage = "Role must be Student or Mentor.")]
    public string Role { get; set; } = string.Empty;
}

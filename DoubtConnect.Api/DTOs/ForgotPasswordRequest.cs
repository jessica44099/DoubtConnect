
using System.ComponentModel.DataAnnotations;

namespace DoubtConnect.Api.DTOs;

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

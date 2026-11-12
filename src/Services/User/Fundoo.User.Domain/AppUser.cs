using System;
using System.Collections.Generic;
using System.Text;

namespace Fundoo.User.Domain
{
    public class AppUser
    {
        public Guid Id { get; set; }
        // Global Unique Identifier
        // Data type used to generate unique IDs - Guid.NewGuid().
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string NormalizedEmail { get; set; } = string.Empty;  // Email.Trim().ToUpperInvariant()
        public string PasswordHash { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public string? TwoFactorSecret { get; set; }   // encrypted store hoga
        public long TwoFactorLastStep { get; set; }    // ek hi code do baar use na ho
        public string? GoogleId { get; set; }
    }
}

using System;

namespace TM.Framework.User.Account.PasswordSecurity
{
    public class PasswordSecurityModel
    {
        public string OldPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public PasswordStrength Strength { get; set; }
        public bool IsTwoFactorEnabled { get; set; }
        public string TwoFactorSecret { get; set; } = string.Empty;
        public DateTime? LastPasswordChangeTime { get; set; }
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum PasswordStrength
    {
        Weak,
        Medium,
        Strong
    }
}


using System;
using System.Security.Cryptography;
using System.Text;

namespace TM.Framework.Common.Helpers
{
    public static class LocalKeyProtector
    {
        private const string Prefix = "ENCV1:";

        public static string Protect(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return plaintext ?? string.Empty;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(plaintext);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(encrypted);
            }
            catch
            {
                return plaintext;
            }
        }

        public static string? TryUnprotect(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
            try
            {
                var base64 = value.Substring(Prefix.Length);
                var encrypted = Convert.FromBase64String(base64);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return null;
            }
        }

        public static bool IsProtected(string? value)
            => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);
    }
}

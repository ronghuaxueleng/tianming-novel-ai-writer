using System;
using System.Security.Cryptography;
using System.Text;

namespace TM.Framework.Common.Helpers.Security
{
    public static class EncryptionHelper
    {
        public static string EncryptApiKey(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
            {
                return string.Empty;
            }

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

                byte[] encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );

                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EncryptionHelper] proc err: {ex.Message}");
                throw new InvalidOperationException("key processing failed", ex);
            }
        }

        public static string DecryptApiKey(string encryptedText)
        {
            if (string.IsNullOrWhiteSpace(encryptedText))
            {
                return string.Empty;
            }

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);

                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EncryptionHelper] read err: {ex.Message}");
                throw new InvalidOperationException("key read failed", ex);
            }
        }

        public static bool IsEncrypted(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                Convert.FromBase64String(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return string.Empty;
            }

            if (apiKey.Length <= 8)
            {
                return new string('*', apiKey.Length);
            }

            return $"{apiKey.Substring(0, 4)}{'*'.ToString().PadLeft(apiKey.Length - 8, '*')}{apiKey.Substring(apiKey.Length - 4)}";
        }
    }
}


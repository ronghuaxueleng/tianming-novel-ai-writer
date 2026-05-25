using System.Text.RegularExpressions;

namespace TM.Framework.Common.Helpers.Validation
{
    public static class ValidationHelper
    {
        private static readonly Regex EmailRegex = new Regex(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled);

        private static readonly Regex PhoneRegex = new Regex(
            @"^1[3-9]\d{9}$",
            RegexOptions.Compiled);

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            return EmailRegex.IsMatch(email);
        }

        public static bool IsValidPhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return false;

            return PhoneRegex.IsMatch(phone);
        }

        public static bool IsValidLength(string value, int minLength, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return minLength == 0;

            int length = value.Length;
            return length >= minLength && length <= maxLength;
        }

        public static string GetEmailErrorMessage(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return string.Empty;

            if (!IsValidEmail(email))
                return "邮箱格式不正确";

            return string.Empty;
        }

        public static string GetPhoneErrorMessage(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return string.Empty;

            if (!IsValidPhone(phone))
                return "手机号格式不正确（请输入中国大陆手机号）";

            return string.Empty;
        }
    }
}


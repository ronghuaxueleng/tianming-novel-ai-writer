using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Zxcvbn;

namespace TM.Framework.Common.Helpers.Validation
{
    public static class PasswordStrengthValidator
    {
        public static PasswordStrength ValidateStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
                return PasswordStrength.Weak;

            var result = Zxcvbn.Core.EvaluatePassword(password);

            return result.Score switch
            {
                0 or 1 => PasswordStrength.Weak,
                2 => PasswordStrength.Medium,
                _ => PasswordStrength.Strong
            };
        }

        public static int GetStrengthScore(string password)
        {
            if (string.IsNullOrEmpty(password))
                return 0;

            var result = Zxcvbn.Core.EvaluatePassword(password);

            return result.Score switch
            {
                0 => 10,
                1 => 30,
                2 => 55,
                3 => 80,
                4 => 100,
                _ => 0
            };
        }

        public static string GetStrengthColor(PasswordStrength strength)
        {
            return strength switch
            {
                PasswordStrength.Weak => "#FF4444",
                PasswordStrength.Medium => "#FFA500",
                PasswordStrength.Strong => "#00C851",
                _ => "#CCCCCC"
            };
        }

        public static string GetStrengthText(PasswordStrength strength)
        {
            return strength switch
            {
                PasswordStrength.Weak => "弱",
                PasswordStrength.Medium => "中",
                PasswordStrength.Strong => "强",
                _ => "未知"
            };
        }

        public static (bool IsValid, string Message) ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return (false, "密码不能为空");

            if (password.Length < 8)
                return (false, "密码长度至少为8个字符");

            var result = Zxcvbn.Core.EvaluatePassword(password);

            if (result.Score < 2)
            {
                var feedback = result.Feedback;
                if (!string.IsNullOrEmpty(feedback?.Warning))
                    return (false, feedback.Warning);

                if (feedback?.Suggestions != null && feedback.Suggestions.Count > 0)
                    return (false, feedback.Suggestions[0]);

                return (false, "密码强度不足，请使用更复杂的密码");
            }

            return (true, "密码符合要求");
        }

        public static PasswordAnalysisResult GetDetailedAnalysis(string password)
        {
            var analysisResult = new PasswordAnalysisResult();

            if (string.IsNullOrEmpty(password))
            {
                analysisResult.OverallStrength = PasswordStrength.Weak;
                analysisResult.Score = 0;
                analysisResult.Suggestions.Add("❌ 密码为空");
                return analysisResult;
            }

            var zxcvbnResult = Zxcvbn.Core.EvaluatePassword(password);

            analysisResult.Length = password.Length;
            analysisResult.LengthScore = Math.Min(password.Length * 2, 30);
            if (password.Length < 8)
                analysisResult.Suggestions.Add("⚠️ 建议密码长度至少为12个字符");
            else if (password.Length >= 16)
                analysisResult.Suggestions.Add("✅ 密码长度优秀");

            analysisResult.HasLowercase = Regex.IsMatch(password, "[a-z]");
            analysisResult.HasUppercase = Regex.IsMatch(password, "[A-Z]");
            analysisResult.HasDigits = Regex.IsMatch(password, "[0-9]");
            analysisResult.HasSpecialChars = Regex.IsMatch(password, "[^a-zA-Z0-9]");

            int typeCount = 0;
            if (analysisResult.HasLowercase) typeCount++;
            if (analysisResult.HasUppercase) typeCount++;
            if (analysisResult.HasDigits) typeCount++;
            if (analysisResult.HasSpecialChars) typeCount++;
            analysisResult.ComplexityScore = typeCount * 10;

            analysisResult.UniqueChars = password.Distinct().Count();
            analysisResult.DiversityRatio = (double)analysisResult.UniqueChars / password.Length;
            analysisResult.DiversityScore = analysisResult.DiversityRatio > 0.5 ? 15 : 0;

            analysisResult.IsCommonPassword = zxcvbnResult.Score <= 1;
            analysisResult.IsOnlyDigits = Regex.IsMatch(password, "^[0-9]+$");
            analysisResult.IsOnlyLetters = Regex.IsMatch(password, "^[a-zA-Z]+$");

            if (zxcvbnResult.Feedback?.Warning is string warning && !string.IsNullOrEmpty(warning))
                analysisResult.Suggestions.Add($"⚠️ {warning}");

            if (zxcvbnResult.Feedback?.Suggestions is { Count: > 0 } suggestions)
            {
                foreach (var suggestion in suggestions)
                    analysisResult.Suggestions.Add($"💡 {suggestion}");
            }

            analysisResult.Entropy = zxcvbnResult.GuessesLog10 * 3.32;

            analysisResult.Score = GetStrengthScore(password);

            analysisResult.OverallStrength = ValidateStrength(password);

            analysisResult.EstimatedCrackTime = FormatCrackTime(zxcvbnResult.CrackTimeDisplay);

            analysisResult.Summary = analysisResult.OverallStrength switch
            {
                PasswordStrength.Weak => $"密码强度：弱（{analysisResult.Score}分）- 容易被破解",
                PasswordStrength.Medium => $"密码强度：中等（{analysisResult.Score}分）- 可以接受",
                PasswordStrength.Strong => $"密码强度：强（{analysisResult.Score}分）- 难以破解",
                _ => "未知"
            };

            return analysisResult;
        }

        private static string FormatCrackTime(CrackTimesDisplay display)
        {
            if (display == null)
                return "未知";

            var time = display.OfflineFastHashing1e10PerSecond;
            if (string.IsNullOrEmpty(time))
                return "未知";

            return time switch
            {
                "less than a second" => "瞬间",
                var t when t.Contains("second") => t.Replace("seconds", "秒").Replace("second", "秒"),
                var t when t.Contains("minute") => t.Replace("minutes", "分钟").Replace("minute", "分钟"),
                var t when t.Contains("hour") => t.Replace("hours", "小时").Replace("hour", "小时"),
                var t when t.Contains("day") => t.Replace("days", "天").Replace("day", "天"),
                var t when t.Contains("month") => t.Replace("months", "个月").Replace("month", "个月"),
                var t when t.Contains("year") => t.Replace("years", "年").Replace("year", "年"),
                var t when t.Contains("centuries") => "数百年",
                _ => time
            };
        }
    }

    public class PasswordAnalysisResult
    {
        public int Score { get; set; }
        public PasswordStrength OverallStrength { get; set; }
        public string Summary { get; set; } = string.Empty;

        public int Length { get; set; }
        public int LengthScore { get; set; }

        public bool HasLowercase { get; set; }
        public bool HasUppercase { get; set; }
        public bool HasDigits { get; set; }
        public bool HasSpecialChars { get; set; }
        public int ComplexityScore { get; set; }

        public int UniqueChars { get; set; }
        public double DiversityRatio { get; set; }
        public int DiversityScore { get; set; }

        public bool IsCommonPassword { get; set; }
        public bool IsOnlyDigits { get; set; }
        public bool IsOnlyLetters { get; set; }

        public double Entropy { get; set; }
        public string EstimatedCrackTime { get; set; } = string.Empty;

        public List<string> Suggestions { get; set; } = new List<string>();
    }

    public enum PasswordStrength
    {
        Weak,
        Medium,
        Strong
    }
}


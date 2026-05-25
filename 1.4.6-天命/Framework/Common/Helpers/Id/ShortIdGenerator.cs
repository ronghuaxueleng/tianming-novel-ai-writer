using System;
using System.Security.Cryptography;
using System.Text;

namespace TM.Framework.Common.Helpers.Id
{
    public static class ShortIdGenerator
    {
        private const int TimestampBits = 41;
        private const int RandomBits = 19;
        private const int Base32Bits = 5;
        private const int IdLength = 12;
        private const ulong RandomMask = (1UL << RandomBits) - 1;
        private static readonly char[] Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ".ToCharArray();

        private static readonly object LockObj = new();
        private static long _lastTimestamp;
        private static ulong _lastRandom;

        public static bool IsLikelyId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (Guid.TryParse(value, out _)) return true;
            if (value.Length == 13 && char.IsUpper(value[0])) return true;
            return false;
        }

        public static string New(string prefix)
        {
            var id = GenerateRandomId();
            return NormalizePrefix(prefix) + id;
        }

        public static Guid NewGuid()
        {
            Span<byte> buffer = stackalloc byte[16];
            RandomNumberGenerator.Fill(buffer);
            return new Guid(buffer);
        }

        public static string NewDeterministic(string prefix, string seed)
        {
            var id = GenerateDeterministicId(prefix, seed);
            return NormalizePrefix(prefix) + id;
        }

        private static string NormalizePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return string.Empty;
            }

            return char.ToUpperInvariant(prefix.Trim()[0]).ToString();
        }

        private static string GenerateRandomId()
        {
            ulong value;
            lock (LockObj)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (timestamp < _lastTimestamp)
                {
                    timestamp = _lastTimestamp + 1;
                }

                var random = NextRandom19();
                if (timestamp == _lastTimestamp && random == _lastRandom)
                {
                    random = (random + 1) & RandomMask;
                }

                _lastTimestamp = timestamp;
                _lastRandom = random;

                value = ((ulong)timestamp << RandomBits) | random;
            }

            return EncodeBase32(value);
        }

        private static string GenerateDeterministicId(string prefix, string seed)
        {
            var input = $"{NormalizePrefix(prefix)}|{seed}";
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);
            ulong value = 0;
            for (int i = 0; i < 8; i++)
            {
                value = (value << 8) | hash[i];
            }

            value &= (1UL << (TimestampBits + RandomBits)) - 1;
            return EncodeBase32(value);
        }

        private static ulong NextRandom19()
        {
            Span<byte> buffer = stackalloc byte[4];
            RandomNumberGenerator.Fill(buffer);
            var value = BitConverter.ToUInt32(buffer);
            return value & RandomMask;
        }

        private static string EncodeBase32(ulong value)
        {
            Span<char> buffer = stackalloc char[IdLength];
            for (int i = IdLength - 1; i >= 0; i--)
            {
                var index = (int)(value & 31UL);
                buffer[i] = Alphabet[index];
                value >>= Base32Bits;
            }

            return new string(buffer);
        }
    }
}

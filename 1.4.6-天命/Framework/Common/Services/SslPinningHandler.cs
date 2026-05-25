using System;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TM.Framework.Common.Services
{
    public static class SslPinningHandler
    {
        private static readonly string[] _fallbackPins = new[]
        {
            "YOUR_SSL_PIN_BASE64_HERE",
        };

        [DllImport("TMProtect.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern bool TMP_CheckSslPin(string pinBase64);

        private static bool _nativePinAvailable = true;

        public static HttpClientHandler CreatePinnedHandler(bool useProxy = true)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = useProxy,
                ServerCertificateCustomValidationCallback = ValidateCertificate
            };
            return handler;
        }

        private static bool ValidateCertificate(
            HttpRequestMessage request,
            X509Certificate2? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                TM.App.Log($"[SSL] err: {sslPolicyErrors}");
                return false;
            }

            if (certificate == null) return false;

            try
            {
                if (CheckPin(certificate)) return true;
                if (chain?.ChainElements != null)
                {
                    foreach (var element in chain.ChainElements)
                    {
                        if (CheckPin(element.Certificate)) return true;
                    }
                }

                TM.App.Log("[SSL] fail");
                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SSL] err: {ex.Message}");
                return false;
            }
        }

        private static bool CheckPin(X509Certificate2 cert)
        {
            var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
            var hash = SHA256.HashData(spki);
            var pin = Convert.ToBase64String(hash);

            if (_nativePinAvailable)
            {
                try
                {
                    return TMP_CheckSslPin(pin);
                }
                catch
                {
                    _nativePinAvailable = false;
                }
            }

            foreach (var expected in _fallbackPins)
            {
                if (pin == expected) return true;
            }
            return false;
        }
    }
}

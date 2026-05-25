using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace TM.Framework.Common.Services
{
    public static class SslPinningHandler
    {
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
            return true;
        }
    }
}

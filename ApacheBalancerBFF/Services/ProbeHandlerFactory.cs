using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using ApacheBalancerBFF.Configuration;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// Builds the HTTP handler the watchdog probes backends with.
/// </summary>
public static class ProbeHandlerFactory
{
    /// <summary>
    /// Creates the handler. When <see cref="WatchdogOptions.CustomRootCertificatePath"/> is set,
    /// the backends' certificates are validated against that root instead of the host's trust
    /// store — the case where a firewall re-signs them on the fly and Apache is the only thing on
    /// the network configured to trust it.
    /// </summary>
    public static SocketsHttpHandler Create(WatchdogOptions options)
    {
        SocketsHttpHandler handler = new()
        {
            // A redirect to a login or error page is not a healthy backend; let the probe see the
            // 302 and judge it.
            AllowAutoRedirect = false,

            // No pooling: every probe opens its own connection. Reuse would let a probe succeed
            // on a socket opened before the trouble started, while every new connection — which
            // is what fresh user traffic needs — stalls. Any lifetime longer than the poll
            // interval lets that happen; zero is the only value that does not depend on the
            // interval. The connect and TLS handshake therefore count against the probe's
            // timeout and latency ceiling, as they do for a client arriving now.
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };

        if (!string.IsNullOrWhiteSpace(options.CustomRootCertificatePath))
        {
            handler.SslOptions.RemoteCertificateValidationCallback =
                BuildCustomRootValidation(options.CustomRootCertificatePath);
        }

        return handler;
    }

    private static RemoteCertificateValidationCallback BuildCustomRootValidation(string certificatePath)
    {
        X509Certificate2 root = LoadCertificate(certificatePath);

        return (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) =>
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            // A name mismatch is never excused by a custom root: it is the one error that would
            // mean the probe is talking to a different host than it thinks.
            if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            {
                return false;
            }

            using X509Chain custom = new();
            custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            custom.ChainPolicy.CustomTrustStore.Add(root);
            custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            using X509Certificate2 presented = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            return custom.Build(presented);
        };
    }

    private static X509Certificate2 LoadCertificate(string path)
    {
        string content = File.ReadAllText(path);

        return content.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(content)
            : X509CertificateLoader.LoadCertificateFromFile(path);
    }
}

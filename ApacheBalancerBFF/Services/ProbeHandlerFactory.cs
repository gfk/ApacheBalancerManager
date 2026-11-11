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

            // Short-lived connections. A pooled connection to a backend that has stopped
            // servicing requests without closing is exactly the state this watchdog exists to
            // catch, and reusing it would just reproduce the problem on the probe side.
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
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

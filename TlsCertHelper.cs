using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VeloUploader;

/// <summary>
/// Helpers for TLS certificate validation and self-signed certificate generation.
/// </summary>
public static class TlsCertHelper
{
    private static readonly string CertsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VeloUploader", "certs");

    /// <summary>
    /// Build an <see cref="HttpClientHandler"/> configured per the current TLS settings:
    /// <list type="bullet">
    ///   <item><see cref="AppSettings.AllowSelfSignedCerts"/> — accept any server certificate (bypasses OS chain validation).</item>
    ///   <item><see cref="AppSettings.TrustedCertPath"/> set — pin to the thumbprint of that specific certificate.</item>
    ///   <item>Neither — default OS chain validation.</item>
    /// </list>
    /// </summary>
    public static HttpClientHandler CreateHandler(AppSettings settings)
    {
        var handler = new HttpClientHandler();

        if (settings.AllowSelfSignedCerts)
        {
            // Accept any server certificate — useful for private self-hosted instances
            // with self-signed certs where OS chain validation would fail.
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        else if (!string.IsNullOrWhiteSpace(settings.TrustedCertPath)
                 && File.Exists(settings.TrustedCertPath))
        {
            X509Certificate2? trusted = null;
            try
            {
                trusted = X509CertificateLoader.LoadCertificateFromFile(settings.TrustedCertPath);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not load trusted cert '{settings.TrustedCertPath}': {ex.Message}");
            }

            if (trusted != null)
            {
                // Pin: only accept the server certificate whose thumbprint matches the saved cert.
                var expectedThumbprint = trusted.Thumbprint;
                handler.ServerCertificateCustomValidationCallback = (_, serverCert, _, _) =>
                    serverCert != null &&
                    string.Equals(serverCert.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
            }
        }

        return handler;
    }

    /// <summary>
    /// Generate a self-signed RSA-2048 certificate valid for 10 years.
    /// Writes two files into %LOCALAPPDATA%\VeloUploader\certs\:
    /// <list type="bullet">
    ///   <item><c>&lt;subjectName&gt;.pfx</c> — certificate + private key for installing on the VELO server.</item>
    ///   <item><c>&lt;subjectName&gt;.crt</c> — public certificate for pinning in the uploader.</item>
    /// </list>
    /// </summary>
    /// <returns>Tuple of (pfxPath, crtPath).</returns>
    public static (string PfxPath, string CrtPath) GenerateSelfSignedCert(string subjectName)
    {
        Directory.CreateDirectory(CertsDir);

        using var rsa = RSA.Create(2048);
        var dn = new X500DistinguishedName($"CN={subjectName}");
        var req = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        // TLS server authentication OID
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], false));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddYears(10);

        using var cert = req.CreateSelfSigned(notBefore, notAfter);

        var safe = MakeSafeFileName(subjectName);
        var pfxPath = Path.Combine(CertsDir, $"{safe}.pfx");
        var crtPath = Path.Combine(CertsDir, $"{safe}.crt");

        File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx));
        File.WriteAllBytes(crtPath, cert.Export(X509ContentType.Cert));

        Logger.Info($"Self-signed cert generated: CN={subjectName}, thumbprint={cert.Thumbprint}, expires={cert.NotAfter:yyyy-MM-dd}");
        return (pfxPath, crtPath);
    }

    /// <summary>Returns a one-line human-readable summary of the certificate at <paramref name="certPath"/>.</summary>
    public static string GetCertInfo(string certPath)
    {
        try
        {
            using var cert = X509CertificateLoader.LoadCertificateFromFile(certPath);
            var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
            var thumb = cert.Thumbprint.Length >= 16 ? cert.Thumbprint[..16] + "…" : cert.Thumbprint;
            return $"CN={cn}  Expires {cert.NotAfter:yyyy-MM-dd}  SHA1:{thumb}";
        }
        catch { return ""; }
    }

    private static string MakeSafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

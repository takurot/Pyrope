using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace Pyrope.GarnetServer.Security
{
    public static class PemCertificateLoader
    {
        public static X509Certificate2 LoadCertificateFromPemFile(string pemPath)
        {
            if (string.IsNullOrWhiteSpace(pemPath)) throw new ArgumentException("PEM path cannot be empty.", nameof(pemPath));
            if (!File.Exists(pemPath)) throw new FileNotFoundException("PEM certificate file not found.", pemPath);
            return X509Certificate2.CreateFromPemFile(pemPath);
        }

        public static X509Certificate2 LoadClientCertificateFromPemFiles(string certPemPath, string keyPemPath)
        {
            if (string.IsNullOrWhiteSpace(certPemPath)) throw new ArgumentException("Certificate PEM path cannot be empty.", nameof(certPemPath));
            if (string.IsNullOrWhiteSpace(keyPemPath)) throw new ArgumentException("Key PEM path cannot be empty.", nameof(keyPemPath));
            if (!File.Exists(certPemPath)) throw new FileNotFoundException("Certificate PEM file not found.", certPemPath);
            if (!File.Exists(keyPemPath)) throw new FileNotFoundException("Key PEM file not found.", keyPemPath);

            return X509Certificate2.CreateFromPemFile(certPemPath, keyPemPath);
        }
    }
}


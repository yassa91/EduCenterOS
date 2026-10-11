using System.Security.Cryptography;

namespace EduCenterOS.UnitTests.Infrastructure;

// Reuse synthetic key material without retaining unmanaged RSA handles for the test process.
internal sealed class TestRsaKey
{
    internal string PrivatePem { get; }
    internal string PublicPem { get; }

    internal TestRsaKey()
    {
        using var key = RSA.Create(2048);
        PrivatePem = key.ExportPkcs8PrivateKeyPem();
        PublicPem = key.ExportSubjectPublicKeyInfoPem();
    }

    internal byte[] Sign(byte[] data)
    {
        using var key = RSA.Create();
        key.ImportFromPem(PrivatePem);

        return key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}

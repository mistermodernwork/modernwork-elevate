using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AppForge.Core.Abstractions;

namespace AppForge.Signing.Verification;

/// <summary>
/// Plattformunabhängige Authenticode-Prüfung in .NET (Plan 3.2b: Herstellersignatur prüfen ohne Windows).
/// PE-Dateien (EXE/DLL/SYS) und PowerShell-Skripte werden vollständig geprüft – CMS-Signatur
/// UND Hash über den Inhalt. MSI/CAB/CAT: CMS-Signatur und Signer, der Inhalts-Hash nur unter Windows.
/// Die Vertrauenskette wird gegen den System-Speicher plus optional zusätzliche Wurzeln geprüft.
/// </summary>
public sealed class AuthenticodeInspector : ISignatureVerifier
{
    private const string SpcIndirectDataOid = "1.3.6.1.4.1.311.2.1.4";
    private const string RfcTimestampOid = "1.3.6.1.4.1.311.3.3.1";
    private const string CounterSignatureOid = "1.2.840.113549.1.9.6";
    private const string PsBegin = "# SIG # Begin signature block";
    private const string PsEnd = "# SIG # End signature block";

    private readonly X509Certificate2Collection _extraRoots;
    private readonly bool _requireTrustedChain;

    public AuthenticodeInspector(X509Certificate2Collection? extraTrustedRoots = null, bool requireTrustedChain = false)
    {
        _extraRoots = extraTrustedRoots ?? [];
        _requireTrustedChain = requireTrustedChain;
    }

    public async Task<SignatureInfo> VerifyAsync(string file, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        return ext switch
        {
            ".ps1" or ".psm1" or ".psd1" or ".ps1xml" or ".vbs" or ".js" => VerifyScript(bytes, ext),
            _ when IsPe(bytes) => VerifyPe(bytes),
            ".msi" or ".msix" or ".appx" or ".cab" or ".cat" => VerifyContainer(bytes, ext),
            _ => new SignatureInfo(SignatureStatus.Unknown, null, null, null, false, $"Dateityp {ext} wird nicht geprüft."),
        };
    }

    internal static bool IsPe(byte[] b) => b.Length > 0x40 && b[0] == 'M' && b[1] == 'Z';

    internal SignatureInfo VerifyPe(byte[] file)
    {
        var peOffset = BitConverter.ToInt32(file, 0x3C);
        if (peOffset <= 0 || peOffset + 24 > file.Length || file[peOffset] != 'P' || file[peOffset + 1] != 'E')
        {
            return new SignatureInfo(SignatureStatus.Unknown, null, null, null, false, "Keine gültige PE-Datei.");
        }

        var optStart = peOffset + 24;
        var magic = BitConverter.ToUInt16(file, optStart);
        var checksumOffset = optStart + 64;
        var ddStart = optStart + (magic == 0x20b ? 112 : 96);
        var certEntry = ddStart + (4 * 8);
        if (certEntry + 8 > file.Length)
        {
            return new SignatureInfo(SignatureStatus.Unknown, null, null, null, false, "PE-Header unvollständig.");
        }

        var certOffset = BitConverter.ToInt32(file, certEntry);
        var certSize = BitConverter.ToInt32(file, certEntry + 4);
        if (certOffset == 0 || certSize == 0)
        {
            return new SignatureInfo(SignatureStatus.NotSigned, null, null, null, false, "Keine Authenticode-Signatur.");
        }

        if (certOffset + certSize > file.Length || certSize < 8)
        {
            return new SignatureInfo(SignatureStatus.HashMismatch, null, null, null, false, "Zertifikatstabelle außerhalb der Datei.");
        }

        var length = BitConverter.ToInt32(file, certOffset);
        var pkcs7 = file.AsSpan(certOffset + 8, Math.Min(length, certSize) - 8).ToArray();

        return Evaluate(pkcs7, alg =>
        {
            using var hash = IncrementalHash.CreateHash(alg);
            hash.AppendData(file, 0, checksumOffset);
            hash.AppendData(file, checksumOffset + 4, certEntry - (checksumOffset + 4));
            hash.AppendData(file, certEntry + 8, certOffset - (certEntry + 8));
            var tail = certOffset + certSize;
            if (tail < file.Length)
            {
                hash.AppendData(file, tail, file.Length - tail);
            }

            return hash.GetHashAndReset();
        });
    }

    internal SignatureInfo VerifyScript(byte[] file, string ext)
    {
        var text = DecodeText(file);
        var (begin, end, prefix) = ext switch
        {
            ".vbs" => ("'' SIG '' Begin signature block", "'' SIG '' End signature block", "'' SIG '' "),
            ".js" => ("// SIG // Begin signature block", "// SIG // End signature block", "// SIG // "),
            ".ps1xml" => ("<!-- SIG # Begin signature block -->", "<!-- SIG # End signature block -->", "<!-- "),
            _ => (PsBegin, PsEnd, "# "),
        };

        var start = text.LastIndexOf(begin, StringComparison.Ordinal);
        if (start < 0)
        {
            return new SignatureInfo(SignatureStatus.NotSigned, null, null, null, false, "Kein Signaturblock.");
        }

        var stop = text.IndexOf(end, start, StringComparison.Ordinal);
        if (stop < 0)
        {
            return new SignatureInfo(SignatureStatus.HashMismatch, null, null, null, false, "Signaturblock unvollständig.");
        }

        var block = text[(start + begin.Length)..stop];
        var base64 = new StringBuilder();
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim('\r', ' ');
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                line = line[prefix.Length..];
            }

            line = line.Replace("-->", string.Empty, StringComparison.Ordinal).Trim();
            base64.Append(line);
        }

        byte[] pkcs7;
        try
        {
            pkcs7 = Convert.FromBase64String(base64.ToString());
        }
        catch (FormatException)
        {
            return new SignatureInfo(SignatureStatus.HashMismatch, null, null, null, false, "Signaturblock ist kein gültiges Base64.");
        }

        // Signiert ist der Inhalt vor dem Block inkl. BOM, ohne das trennende CRLF, als UTF-16LE.
        var content = text[..start];
        if (content.EndsWith("\r\n", StringComparison.Ordinal))
        {
            content = content[..^2];
        }

        return Evaluate(pkcs7, alg =>
        {
            using var hash = IncrementalHash.CreateHash(alg);
            hash.AppendData(Encoding.Unicode.GetBytes(content));
            return hash.GetHashAndReset();
        });
    }

    internal SignatureInfo VerifyContainer(byte[] file, string ext)
    {
        // MSI: Stream \x05DigitalSignature im OLE-Container; ohne OLE-Parser suchen wir die PKCS#7-Struktur
        // anhand der SignedData-OID. Der Inhalts-Hash wird hier nicht nachgerechnet (Windows: signtool verify).
        var oid = new byte[] { 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x02 };
        var idx = file.AsSpan().IndexOf(oid);
        if (idx < 4)
        {
            return new SignatureInfo(SignatureStatus.NotSigned, null, null, null, false, "Keine Signatur gefunden.");
        }

        for (var back = 2; back <= 6 && idx - back >= 0; back++)
        {
            var start = idx - back;
            if (file[start] != 0x30)
            {
                continue;
            }

            try
            {
                var reader = new AsnReader(file.AsMemory(start), AsnEncodingRules.BER);
                var seq = reader.ReadEncodedValue();
                var result = Evaluate(seq.ToArray(), null);
                return result with { StatusMessage = (result.StatusMessage + $" Inhalts-Hash ({ext}) wird nur unter Windows geprüft.").Trim() };
            }
            catch (AsnContentException)
            {
                // nächster Versuch
            }
        }

        return new SignatureInfo(SignatureStatus.Unknown, null, null, null, false, "Signaturstruktur nicht lesbar.");
    }

    private SignatureInfo Evaluate(byte[] pkcs7, Func<HashAlgorithmName, byte[]>? computeDigest)
    {
        var cms = new SignedCms();
        try
        {
            cms.Decode(pkcs7);
        }
        catch (CryptographicException ex)
        {
            return new SignatureInfo(SignatureStatus.HashMismatch, null, null, null, false, "PKCS#7 ungültig: " + ex.Message);
        }

        if (cms.SignerInfos.Count == 0)
        {
            return new SignatureInfo(SignatureStatus.NotSigned, null, null, null, false, "Keine Signer.");
        }

        var signer = cms.SignerInfos[0];
        var cert = signer.Certificate;
        var hasTimestamp = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>()
            .Any(a => a.Oid.Value is RfcTimestampOid or CounterSignatureOid);

        try
        {
            cms.CheckSignature(verifySignatureOnly: true);
        }
        catch (CryptographicException ex)
        {
            return new SignatureInfo(SignatureStatus.HashMismatch, cert?.Subject, cert?.Issuer, cert?.Thumbprint, hasTimestamp, "Signatur ungültig: " + ex.Message);
        }

        if (computeDigest is not null)
        {
            if (cms.ContentInfo.ContentType.Value != SpcIndirectDataOid)
            {
                return new SignatureInfo(SignatureStatus.HashMismatch, cert?.Subject, cert?.Issuer, cert?.Thumbprint, hasTimestamp, "Kein Authenticode-Inhalt.");
            }

            var (alg, expected) = ReadIndirectDigest(cms.ContentInfo.Content);
            var actual = computeDigest(alg);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return new SignatureInfo(SignatureStatus.HashMismatch, cert?.Subject, cert?.Issuer, cert?.Thumbprint, hasTimestamp, "Inhalt wurde nach dem Signieren verändert.");
            }
        }

        if (cert is null)
        {
            return new SignatureInfo(SignatureStatus.Unknown, null, null, null, hasTimestamp, "Signer-Zertifikat fehlt.");
        }

        var chainStatus = CheckChain(cert, cms.Certificates, hasTimestamp);
        return new SignatureInfo(chainStatus.Status, cert.Subject, cert.Issuer, cert.Thumbprint, hasTimestamp, chainStatus.Message);
    }

    private (SignatureStatus Status, string Message) CheckChain(X509Certificate2 cert, X509Certificate2Collection extra, bool hasTimestamp)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ExtraStore.AddRange(extra);
        if (_extraRoots.Count > 0)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(_extraRoots);
        }

        if (hasTimestamp)
        {
            // Mit Zeitstempel bleibt die Signatur nach Ablauf des Zertifikats gültig.
            chain.ChainPolicy.VerificationFlags |= X509VerificationFlags.IgnoreNotTimeValid;
        }

        var ok = chain.Build(cert);
        if (ok)
        {
            return (SignatureStatus.Valid, "Signatur gültig, Kette vertrauenswürdig.");
        }

        var status = string.Join(", ", chain.ChainStatus.Select(s => s.Status.ToString()));
        if (chain.ChainStatus.Any(s => s.Status.HasFlag(X509ChainStatusFlags.NotTimeValid)))
        {
            return (SignatureStatus.Expired, "Zertifikat abgelaufen und kein Zeitstempel: " + status);
        }

        return _requireTrustedChain
            ? (SignatureStatus.UntrustedRoot, "Kette nicht vertrauenswürdig: " + status)
            : (SignatureStatus.Valid, "Signatur und Inhalt gültig; Kette auf diesem System nicht vertrauenswürdig (" + status + ").");
    }

    internal static (HashAlgorithmName Alg, byte[] Digest) ReadIndirectDigest(byte[] content)
    {
        var reader = new AsnReader(content, AsnEncodingRules.BER);
        var spc = reader.ReadSequence();
        spc.ReadEncodedValue(); // SpcAttributeTypeAndOptionalValue
        var digestInfo = spc.ReadSequence();
        var algSeq = digestInfo.ReadSequence();
        var oid = algSeq.ReadObjectIdentifier();
        var digest = digestInfo.ReadOctetString();
        var alg = oid switch
        {
            "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
            "2.16.840.1.101.3.4.2.2" => HashAlgorithmName.SHA384,
            "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
            "1.3.14.3.2.26" => HashAlgorithmName.SHA1,
            _ => throw new CryptographicException($"Hash-Algorithmus {oid} wird nicht unterstützt."),
        };
        return (alg, digest);
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            // Das BOM gehört zum signierten Inhalt (wie beim PowerShell-SIP unter Windows).
            return "\uFEFF" + Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return "\uFEFF" + Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        return Encoding.UTF8.GetString(bytes);
    }
}

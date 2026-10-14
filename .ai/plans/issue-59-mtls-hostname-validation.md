# Implementation Plan - Issue #59: mTLS hostname validation silently discarded in SidecarExtensions

## 1. Background & Problem Analysis
Issue #59 identifies a security vulnerability in `SidecarExtensions.cs`:
- In the `RemoteCertificateValidationCallback` for mTLS connections to the AI sidecar:
  - If `skipServerNameValidation` is false, `sslPolicyErrors` can contain `RemoteCertificateNameMismatch`.
  - However, the code modified `sslPolicyErrors` and then discarded it completely, deciding validity solely based on `customChain.Build(serverCert)`.
  - Because `X509Chain.Build()` only validates the certificate signature against the CA and does not validate hostname matching, any server presenting any certificate signed by the CA (even for an unrelated host) was accepted.
  - This allows a server identity spoofing / man-in-the-middle vulnerability if multiple services share the CA.

## 2. Technical Design & Changes

### 2.1 Refactor RemoteCertificateValidationCallback
In `src/Pyrope.GarnetServer/Extensions/SidecarExtensions.cs`:
- Extract certificate validation logic into an `internal static bool ValidateRemoteCertificate(...)`:
  ```csharp
  internal static bool ValidateRemoteCertificate(
      X509Certificate? certificate,
      SslPolicyErrors sslPolicyErrors,
      X509Certificate2? caCert,
      bool skipServerNameValidation)
  ```
- Validation rules:
  1. If `certificate == null`, return `false`.
  2. If `(sslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0`, return `false`.
  3. If `caCert == null`, return `sslPolicyErrors == SslPolicyErrors.None`.
  4. If `!skipServerNameValidation` and `(sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0`, return `false`.
  5. Build `customChain` against `caCert`:
     - Set `customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck`.
     - Set `customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust`.
     - Add `caCert` to `customChain.ChainPolicy.CustomTrustStore`.
     - Return `customChain.Build(serverCert)`.
- Use this method in `RemoteCertificateValidationCallback`:
  ```csharp
  RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
      ValidateRemoteCertificate(certificate, sslPolicyErrors, caCert, skipServerNameValidation)
  ```

## 3. Test Strategy (TDD)
Create `tests/Pyrope.GarnetServer.Tests/Extensions/SidecarExtensionsTests.cs`:
1. `ValidateRemoteCertificate_NullCertificate_ReturnsFalse`: When certificate is null, returns false.
2. `ValidateRemoteCertificate_HostnameMismatch_WhenValidationRequired_ReturnsFalse`: When `sslPolicyErrors` has `RemoteCertificateNameMismatch` and `skipServerNameValidation == false`, returns false even if chain would be valid.
3. `ValidateRemoteCertificate_HostnameMismatch_WhenSkipped_EvaluatesChain`: When `sslPolicyErrors` has `RemoteCertificateNameMismatch` and `skipServerNameValidation == true`, continues to chain validation.
4. `ValidateRemoteCertificate_CertificateNotAvailable_ReturnsFalse`: When `sslPolicyErrors` has `RemoteCertificateNotAvailable`, returns false.
5. `ValidateRemoteCertificate_ValidCertAndMatchingHost_ReturnsTrue`: When cert is signed by CA and hostname matches (`sslPolicyErrors` only has `RemoteCertificateChainErrors`), returns true.
6. `ValidateRemoteCertificate_UntrustedCert_ReturnsFalse`: When cert is not signed by CA, returns false.

## 4. Acceptance Criteria
- Unit tests pass.
- `./scripts/check_quality.sh` passes 100%.
- Hostname validation is strictly enforced when `skipServerNameValidation` is false.

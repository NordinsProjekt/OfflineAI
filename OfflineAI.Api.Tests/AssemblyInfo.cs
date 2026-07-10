// OfflineAI.Api is Windows-only (Infrastructure.Data.Dapper uses WindowsIdentity to grant DB access),
// so this test project must be marked the same way to avoid CA1416 platform-compatibility errors.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

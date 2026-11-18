using FieldVisit.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

var cases = new (int?, string)[]
{
    (18456, "SQL_AUTH_OR_DOSGUARD_UNKNOWN"),
    (4060, "DATABASE_ACCESS_OR_UNAVAILABLE"),
    (53, "NETWORK_OR_DNS_UNKNOWN"),
    (11001, "NETWORK_OR_DNS_UNKNOWN"),
    (10060, "NETWORK_OR_DNS_UNKNOWN"),
    (258, "NETWORK_OR_DNS_UNKNOWN"),
    (-2, "TIMEOUT_UNKNOWN"),
    (null, "SQL_CONNECTION_UNKNOWN"),
    (0, "SQL_CONNECTION_UNKNOWN")
};
foreach (var (code, expected) in cases)
    if (SqlConnectionFailureClassifier.Category(code) != expected)
        throw new InvalidOperationException("CLASSIFICATION_CONTRACT_FAIL");
_ = new SqlConnectionFailureClassifier(NullLogger<SqlConnectionFailureClassifier>.Instance);
Console.WriteLine("PATHB_SAFE_DIAGNOSTICS_BEHAVIOR=PASS");

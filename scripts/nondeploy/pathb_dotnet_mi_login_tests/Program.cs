// Offline behavior tests only. No token, SQL, HTTP or Azure.
static void Check(string url, bool expected)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        throw new Exception("Invalid synthetic test URI");
    bool actual = IdentityHostPolicy.IsLocalIpv4Literal(uri.Host);
    if (actual != expected)
        throw new Exception("HOST_POLICY_TEST_FAILED");
}

Check("http://127.0.0.1:41741/MSI/token", true);
Check("http://127.1.2.3:41741/MSI/token", true);
Check("http://169.254.129.2:41741/MSI/token", true);
Check("http://169.254.1.1:41741/MSI/token", true);
Check("http://localhost:41741/MSI/token", false);
Check("http://example.com:41741/MSI/token", false);
Check("http://10.90.2.4:41741/MSI/token", false);
Check("http://192.168.1.1:41741/MSI/token", false);
Check("http://172.16.0.1:41741/MSI/token", false);
Check("http://169.253.255.255:41741/MSI/token", false);
Check("http://169.255.0.1:41741/MSI/token", false);
Check("http://8.8.8.8:41741/MSI/token", false);
Check("http://[::1]:41741/MSI/token", false);
Check("http://[fe80::1]:41741/MSI/token", false);
Console.WriteLine("HOST_POLICY_BEHAVIOR=PASS");

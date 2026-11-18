// Pure, side-effect-free local endpoint host policy. No DNS, token or network operations.
using System.Net;
using System.Net.Sockets;

internal static class IdentityHostPolicy
{
    internal static bool IsLocalIpv4Literal(string host)
    {
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254);
    }
}

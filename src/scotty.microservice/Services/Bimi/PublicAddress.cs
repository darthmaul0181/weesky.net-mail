using System.Net;

namespace weesky.Scotty.Microservice.Services.Bimi;

internal static class PublicAddress
{
    private static readonly byte[] Nat64Prefix = [0, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4) return IsPublicV4(bytes);
        if (bytes.AsSpan(0, 12).SequenceEqual(Nat64Prefix) || !bytes.AsSpan(0, 12).ContainsAnyExcept((byte)0)) return IsPublicV4(bytes[12..]);
        if (bytes[0] == 0x20 && bytes[1] == 0x02) return IsPublicV4(bytes[2..6]);
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0) return false;

        return !(IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast
            || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8));
    }

    private static bool IsPublicV4(byte[] b) => !(b[0] is 0 or 10 or 127 or >= 224
        || (b[0] == 100 && b[1] is >= 64 and <= 127)
        || (b[0] == 169 && b[1] == 254)
        || (b[0] == 172 && b[1] is >= 16 and <= 31)
        || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
        || (b[0] == 192 && b[1] == 168)
        || (b[0] == 198 && b[1] is 18 or 19));
}

namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// One address line as bound from the wire. Settable, not a record: <see cref="ContactLineJsonConverter"/>
/// fills it from either a bare JSON string or an object. The implicit conversion from
/// <see cref="string"/> is what lets the legacy shape the live frontend still posts —
/// <c>["a@b.c"]</c> — and a hand-built <see cref="ContactRequest"/> agree on the same payload.
/// </summary>
public sealed class ContactEmailPayload
{
    /// <summary>The card position this line replaces. Null = a new line (decision 4).</summary>
    public int? Position { get; set; }

    public string? Address { get; set; }

    public string? Type { get; set; }

    public static implicit operator ContactEmailPayload(string address) => new() { Address = address };
}

/// <summary>One phone line as bound from the wire — no legacy string shape, this field is new.</summary>
public sealed class ContactPhonePayload
{
    public int? Position { get; set; }

    public string? Number { get; set; }

    public string? Type { get; set; }
}

/// <summary>One postal address line as bound from the wire.</summary>
public sealed class ContactAddressPayload
{
    public int? Position { get; set; }

    public string? Type { get; set; }

    public string? PoBox { get; set; }

    public string? Extended { get; set; }

    public string? Street { get; set; }

    public string? Locality { get; set; }

    public string? Region { get; set; }

    public string? PostalCode { get; set; }

    public string? Country { get; set; }
}

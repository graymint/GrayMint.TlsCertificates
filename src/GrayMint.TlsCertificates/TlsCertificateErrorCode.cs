namespace GrayMint.TlsCertificates;

/// <summary>Why a domain's last order failed, so a host can tell its user what to fix in words of its own.</summary>
public enum TlsCertificateErrorCode
{
    /// <summary>The domain has no public address: it points nowhere yet, or to a private address.</summary>
    NoAddress,

    /// <summary>Nothing answered on port 80 of the domain's address.</summary>
    NotReached,

    /// <summary>Something other than the host answered on port 80 of the domain, such as a proxy in front of it.</summary>
    OtherAnswer,

    /// <summary>The authority found the challenge invalid; the error's message has its reason.</summary>
    Refused,

    /// <summary>The authority did not decide in time. It may still sign: the next order asks again.</summary>
    NoDecision,

    /// <summary>Anything else, such as the authority unreachable; the host's log has the cause.</summary>
    Failed
}

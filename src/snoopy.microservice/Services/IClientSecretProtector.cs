namespace weesky.Snoopy.Microservice.Services;

/// <summary>Protects the OAuth client secret of an external domain, and nothing else.</summary>
public interface IClientSecretProtector : ISecretProtector;

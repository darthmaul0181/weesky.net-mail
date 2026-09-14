namespace weesky.Snoopy.Microservice.Services;

/// <summary>Protects the password of the service account that sends calendar invitations, and nothing else.</summary>
public interface IServiceAccountSecretProtector : ISecretProtector;

namespace Mitsuke.Core;

/// <summary>One delivery channel (Discord for testing, then email, push, SMS).</summary>
public interface INotifier
{
    string Channel { get; }

    Task SendAsync(string message, CancellationToken cancellationToken = default);
}

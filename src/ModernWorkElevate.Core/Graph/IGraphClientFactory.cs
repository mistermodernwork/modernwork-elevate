namespace ModernWorkElevate.Core.Graph;

using Microsoft.Graph;
using ModernWorkElevate.Core.Auth;

/// <summary>
/// Creates configured <see cref="GraphServiceClient"/> instances. All Graph access
/// in ModernWork Elevate goes through a client built here — never a raw <c>HttpClient</c>.
/// </summary>
public interface IGraphClientFactory
{
    /// <summary>
    /// Builds (or returns a cached) <see cref="GraphServiceClient"/> pinned to
    /// <paramref name="account"/>. Subsequent calls with the same account return
    /// the same client so HTTP connection pooling and Kiota middleware state are
    /// preserved.
    /// </summary>
    GraphServiceClient CreateFor(SignedInAccount account);
}

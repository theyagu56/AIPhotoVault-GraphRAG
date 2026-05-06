using PhotoVault.Core.Domain;

namespace PhotoVault.Core.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetPendingAsync(CancellationToken ct = default);
    Task<string> UpsertAsync(User user, CancellationToken ct = default);
    Task ApproveAsync(string id, string adminId, CancellationToken ct = default);
    Task RejectAsync(string id, string adminId, CancellationToken ct = default);
    Task UpdateLastLoginAsync(string id, CancellationToken ct = default);
    Task<bool> IsAdminAsync(string id, CancellationToken ct = default);
    Task<bool> HasAnyAdminAsync(CancellationToken ct = default);
}

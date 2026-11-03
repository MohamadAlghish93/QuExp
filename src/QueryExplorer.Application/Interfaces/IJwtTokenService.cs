using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
}

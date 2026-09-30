using System.Data;

namespace Eisai.Infrastructure.Persistence.Connection;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

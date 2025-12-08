#region

using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace Mehrak.Infrastructure.Migrations;

public class UserRepositoryMongo
{
    private readonly IMongoCollection<MongoUserModel> m_Users;
    private readonly ILogger<UserRepositoryMongo> m_Logger;

    public UserRepositoryMongo(MongoDbService mongoDbService, ILogger<UserRepositoryMongo> logger)
    {
        m_Users = mongoDbService.Users;
        m_Logger = logger;
    }
}

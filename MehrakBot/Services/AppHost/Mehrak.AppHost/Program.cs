var builder = DistributedApplication.CreateBuilder(args);

// --- Secrets / Parameters ---
var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var redisPassword = builder.AddParameter("redis-password", secret: true);
var discordToken = builder.AddParameter("discord-token", secret: true);
var seaweedSecretKey = builder.AddParameter("seaweed-secret-key", secret: true);
var clickhouseUser = builder.AddParameter("clickhouse-user");
var clickhousePassword = builder.AddParameter("clickhouse-password", secret: true);
var dashboardAdminDiscordId = builder.AddParameter("dashboard-admin-discord-id");
var dashboardOrigin = builder.AddParameter("dashboard-origin");

// --- Infrastructure ---

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .AddDatabase("mehrakdb", databaseName: "mehrak_dev");

var redis = builder.AddRedis("redis", password: redisPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// SeaweedFS cluster
var seaweedMaster = builder.AddContainer("seaweed-master", "chrislusf/seaweedfs", "4.13")
    .WithArgs("master", "-mdir=/data", "-ip=seaweed-master", "-ip.bind=0.0.0.0")
    .WithEndpoint(port: 9333, targetPort: 9333, name: "master-http")
    .WithEndpoint(port: 19333, targetPort: 19333, name: "master-grpc")
    .WithLifetime(ContainerLifetime.Persistent);

var seaweedVolume = builder.AddContainer("seaweed-volume", "chrislusf/seaweedfs", "4.13")
    .WithArgs("volume", "-mserver=seaweed-master:9333", "-dir=/data", "-ip=seaweed-volume", "-preStopSeconds=1")
    .WithEndpoint(port: 8080, targetPort: 8080, name: "volume-http")
    .WithEndpoint(port: 18080, targetPort: 18080, name: "volume-grpc")
    .WithLifetime(ContainerLifetime.Persistent)
    .WaitFor(seaweedMaster);

var seaweedFiler = builder.AddContainer("seaweed-filer", "chrislusf/seaweedfs", "4.13")
    .WithArgs("filer", "-master=seaweed-master:9333", "-ip=seaweed-filer")
    .WithEndpoint(port: 8888, targetPort: 8888, name: "filer-http")
    .WithEndpoint(port: 18888, targetPort: 18888, name: "filer-grpc")
    .WithLifetime(ContainerLifetime.Persistent)
    .WaitFor(seaweedMaster)
    .WaitFor(seaweedVolume);

var seaweedS3 = builder.AddContainer("seaweed-s3", "chrislusf/seaweedfs", "4.13")
    .WithArgs("s3", "-filer=seaweed-filer:8888", "-ip.bind=0.0.0.0", "-port=8333", "-config=/etc/seaweedfs/s3.json")
    .WithEndpoint(port: 8333, targetPort: 8333, name: "s3")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithBindMount("seaweed-s3/s3.json", "/etc/seaweedfs/s3.json")
    .WaitFor(seaweedFiler);

// ClickHouse
var clickhouse = builder.AddContainer("clickhouse", "clickhouse/clickhouse-server", "26.2")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("CLICKHOUSE_USER", clickhouseUser)
    .WithEnvironment("CLICKHOUSE_PASSWORD", clickhousePassword)
    .WithEnvironment("CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT", "1")
    .WithEndpoint(port: 8123, targetPort: 8123, name: "http")
    .WithEndpoint(port: 9000, targetPort: 9000, name: "native");

// --- Application Services ---

var imageProcessor = builder.AddProject<Projects.Mehrak_ImageProcessor>("image-processor")
    .WaitFor(seaweedS3);

var application = builder.AddProject<Projects.Mehrak_Application>("application")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(imageProcessor)
    .WithEnvironment("Storage__ServiceURL", "http://seaweed-s3:8333")
    .WithEnvironment("Storage__SecretKey", seaweedSecretKey)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(seaweedS3);

var bot = builder.AddProject<Projects.Mehrak_Bot>("bot")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(application)
    .WithEnvironment("Discord__Token", discordToken)
    .WithEnvironment("Storage__ServiceURL", "http://seaweed-s3:8333")
    .WithEnvironment("Storage__SecretKey", seaweedSecretKey)
    .WithEnvironment("ClickHouse__Host", "clickhouse")
    .WithEnvironment("ClickHouse__Username", clickhouseUser)
    .WithEnvironment("ClickHouse__Password", clickhousePassword)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(application);

var dashboard = builder.AddProject<Projects.Mehrak_Dashboard>("dashboard")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(application)
    .WithReference(imageProcessor)
    .WithEnvironment("Storage__ServiceURL", "http://seaweed-s3:8333")
    .WithEnvironment("Storage__SecretKey", seaweedSecretKey)
    .WithEnvironment("SeaweedFiler__BaseUrl", "http://seaweed-filer:8888")
    .WithEnvironment("Dashboard__AdminDiscordId", dashboardAdminDiscordId)
    .WithEnvironment("Dashboard__Origin", dashboardOrigin)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(application)
    .WaitFor(imageProcessor);

builder.Build().Run();

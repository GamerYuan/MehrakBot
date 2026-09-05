using Mehrak.ImageProcessor.Shared.Services;
using Mehrak.ServiceDefaults;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
        }

        builder.AddSerilogOtlp("MehrakImageProcessor");

        builder.Services.Configure<NsfwClassifierOptions>(builder.Configuration.GetSection("NsfwClassifier"));

        builder.Services.AddSingleton<INsfwClassifier, NsfwClassifier>();
        builder.Services.AddSingleton<GenshinWeaponImageProcessor>();
        builder.Services.AddSingleton<PortraitImageMatcher>();

        builder.Services.AddGrpc(options =>
        {
            // Byte limits only bound transport size (8 MB uploads plus protobuf headroom).
            // Decoded pixel/dimension budgets and native concurrency bounds are enforced
            // in NsfwClassifier before and after native decoding.
            options.MaxReceiveMessageSize = 12 * 1024 * 1024;
            options.MaxSendMessageSize = 12 * 1024 * 1024;
        });

        var app = builder.Build();

        app.MapDefaultEndpoints();

        // Eagerly load the NSFW classifier model at startup
        app.Services.GetRequiredService<INsfwClassifier>();

        app.MapGrpcService<GrpcImageProcessorService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");

        app.Run();
    }
}

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

        builder.Services.AddGrpc();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        // Eagerly load the NSFW classifier model at startup
        app.Services.GetRequiredService<INsfwClassifier>();

        app.MapGrpcService<GrpcImageProcessorService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");

        app.Run();
    }
}

using EduCenterOS.Api.Configuration;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = ApiConfiguration.CreateBuilder(args);
        builder.AddApiServices();

        var app = builder.Build();
        app.UseApiPipeline();
        app.MapApiEndpoints();

        app.Logger.LogInformation(new EventId(1000, "HostStarting"),
            "Starting API host in {Environment}.", app.Environment.EnvironmentName);
        await app.RunAsync();
    }
}

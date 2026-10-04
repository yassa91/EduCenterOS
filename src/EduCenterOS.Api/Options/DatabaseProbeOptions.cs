namespace EduCenterOS.Api.Options;

internal sealed class DatabaseProbeOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 3;
}

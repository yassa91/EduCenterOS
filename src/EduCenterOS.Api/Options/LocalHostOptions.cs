namespace EduCenterOS.Api.Options;

internal sealed class LocalHostOptions
{
    public int Port { get; set; } = 5100;
    public int HttpsPort { get; set; } = 5101;

    internal static LocalHostOptions BindSafely(IConfiguration configuration)
    {
        try
        {
            var options = configuration.GetSection("Platform:Host").Get<LocalHostOptions>(binder => binder.ErrorOnUnknownConfiguration = true) ?? new();

            if (options.Port is < 1024 or > 65535 || options.HttpsPort is < 1024 or > 65535 || options.Port == options.HttpsPort)
                throw new InvalidOperationException();

            return options;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
        {
            throw new InvalidOperationException("Configuration.InvalidHostSettings: Platform:Host requires a valid bounded port and known keys.");
        }
    }
}

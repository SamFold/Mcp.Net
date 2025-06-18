using Mcp.Net.WebUi.Startup;

namespace Mcp.Net.WebUi;

public class Program
{
    public static async Task Main(string[] args)
    {
        var startup = new WebUiStartup();
        var app = await startup.CreateApplicationAsync(args);
        app.Run();
    }
}

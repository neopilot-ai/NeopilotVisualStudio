using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;

namespace NeopilotVS.Commands;

[Command(PackageIds.RestartLanguageServer)]
internal sealed class CommandRestartLanguageServer : BaseCommand<CommandRestartLanguageServer>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await NeopilotVSPackage.Instance.LogAsync("Restarting Language Server...");
        await NeopilotVSPackage.Instance.LanguageServer.StopAsync();
        await NeopilotVSPackage.Instance.LanguageServer.StartAsync();
    }
}

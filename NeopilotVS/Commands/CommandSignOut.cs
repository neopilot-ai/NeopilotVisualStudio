using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;

namespace NeopilotVS.Commands;

[Command(PackageIds.SignOut)]
internal sealed class CommandSignOut : BaseCommand<CommandSignOut>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await NeopilotVSPackage.Instance.LanguageServer.SignOutAsync();
    }
}

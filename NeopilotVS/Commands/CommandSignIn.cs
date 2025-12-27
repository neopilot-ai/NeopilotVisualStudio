using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;

namespace NeopilotVS.Commands;

[Command(PackageIds.SignIn)]
internal sealed class CommandSignIn : BaseCommand<CommandSignIn>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await NeopilotVSPackage.Instance.LanguageServer.SignInAsync();
    }
}

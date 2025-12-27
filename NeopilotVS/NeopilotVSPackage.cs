global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.Win32;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace NeopilotVS;

//[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
//[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)] //
// VisibilityConstraints example

[Guid(PackageGuids.NeopilotVSString)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(SettingsPage), "Neopilot", "Neopilot", 0, 0, true)]
[ProvideToolWindow(
    typeof(ChatToolWindow), MultiInstances = false, Style = VsDockStyle.Tabbed,
    Orientation = ToolWindowOrientation.Right,
    Window =
        "{3AE79031-E1BC-11D0-8F78-00A0C9110057}")] // default docking window, magic string for the
                                                   // guid of
                                                   // VSConstants.StandardToolWindows.SolutionExplorer
public sealed class NeopilotVSPackage : ToolkitPackage
{
    internal static NeopilotVSPackage? Instance { get; private set; }

    private NotificationInfoBar NotificationAuth;

    public OutputWindow OutputWindow;
    public SettingsPage SettingsPage;
    public LanguageServer LanguageServer;

    protected override async Task InitializeAsync(CancellationToken cancellationToken,
                                                  IProgress<ServiceProgressData> progress)
    {
        Instance = this;

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        LanguageServer = new LanguageServer();
        OutputWindow = new OutputWindow();
        NotificationAuth = new NotificationInfoBar();

        try
        {
            SettingsPage = GetDialogPage(typeof(SettingsPage)) as SettingsPage;
        }
        catch (Exception ex)
        {
            await LogAsync($"NeopilotVSPackage.InitializeAsync: Failed to get settings page; Exception {ex}");
        }

        if (SettingsPage == null)
        {
            await LogAsync(
                $"NeopilotVSPackage.InitializeAsync: Failed to get settings page, using the default settings");
            SettingsPage = new SettingsPage();
        }

        try
        {
            await this.RegisterCommandsAsync();
        }
        catch (Exception ex)
        {
            await LogAsync(
                $"NeopilotVSPackage.InitializeAsync: Failed to register commands; Exception {ex}");
            await VS.MessageBox.ShowErrorAsync("Neopilot: Failed to register commands.",
                                               "Neopilot might not work correctly. Please check " +
                                                   "the output window for more details.");
        }

        try
        {
            await base.InitializeAsync(cancellationToken, progress);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _ = CodeLensConnectionHandler.AcceptCodeLensConnections();
        }
        catch (Exception ex)
        {
            await LogAsync("Neopilot Error" + ex);
            throw;
        }

        await LanguageServer.InitializeAsync();
        await LogAsync("Neopilot Extension for Visual Studio");
    }

    protected override void Dispose(bool disposing)
    {
        LanguageServer.Dispose();
        base.Dispose(disposing);
    }

    public static void EnsurePackageLoaded()
    {
        if (Instance != null) return;

        ThreadHelper.JoinableTaskFactory.Run(EnsurePackageLoadedAsync);
    }

    public static async Task EnsurePackageLoadedAsync()
    {
        if (Instance != null) return;

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        IVsShell vsShell = (IVsShell)ServiceProvider.GlobalProvider.GetService(typeof(IVsShell)) ??
                           throw new NullReferenceException();

        Guid guidPackage = new(PackageGuids.NeopilotVSString);
        if (vsShell.IsPackageLoaded(ref guidPackage, out var _) == VSConstants.S_OK) return;

        if (vsShell.LoadPackage(ref guidPackage, out var _) != VSConstants.S_OK)
            throw new NullReferenceException();
    }

    public async Task UpdateSignedInStateAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync();

        if ((await GetServiceAsync(typeof(IMenuCommandService)))is OleMenuCommandService cmdService)
        {
            MenuCommand? commandSignIn =
                cmdService.FindCommand(new CommandID(PackageGuids.NeopilotVS, PackageIds.SignIn));
            MenuCommand? commandSignOut =
                cmdService.FindCommand(new CommandID(PackageGuids.NeopilotVS, PackageIds.SignOut));
            MenuCommand? commandEnterToken = cmdService.FindCommand(
                new CommandID(PackageGuids.NeopilotVS, PackageIds.EnterAuthToken));

            if (commandSignIn != null) commandSignIn.Visible = !IsSignedIn();
            if (commandSignOut != null) commandSignOut.Visible = IsSignedIn();
            if (commandEnterToken != null) commandEnterToken.Visible = !IsSignedIn();
        }

        // notify the user they need to sign in
        if (!IsSignedIn())
        {
            KeyValuePair<string, Action>[] actions = [
                new KeyValuePair<string, Action>("Sign in",
                                                 delegate {
                                                     ThreadHelper.JoinableTaskFactory
                                                         .RunAsync(LanguageServer.SignInAsync)
                                                         .FireAndForget(true);
                                                 }),
                new KeyValuePair<string, Action>(
                    "Use authentication token",
                    delegate { new EnterTokenDialogWindow().ShowDialog(); }),
            ];

            NotificationAuth.Show("[Neopilot] To enable Neopilot, please sign in to your account",
                                  KnownMonikers.AddUser,
                                  true,
                                  null,
                                  actions);
        }
        else { await NotificationAuth.CloseAsync(); }

        ChatToolWindow.Instance?.Reload();
    }

    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Instance?.Log($"Could not open in browser: {ex}");
            VS.MessageBox.Show(
                "Neopilot: Failed to open browser",
                $"Please use this URL instead (you can copy from the output window):\n{url}");
        }
    }

    public string GetAppDataPath() => Utilities.PathProvider.GetAppDataPath();

    public string GetLanguageServerFolder() => Utilities.PathProvider.GetLanguageServerFolder(LanguageServer.GetVersion());

    public string GetLanguageServerPath() => Utilities.PathProvider.GetLanguageServerPath(LanguageServer.GetVersion(), SettingsPage.EnterpriseMode);

    public string GetDatabaseDirectory() => Utilities.PathProvider.GetDatabaseDirectory();

    public string GetAPIKeyPath() => Utilities.PathProvider.GetAPIKeyPath();

    public bool IsSignedIn() { return LanguageServer.GetKey().Length > 0; }
    public bool HasEnterprise() { return SettingsPage.EnterpriseMode; }

    internal void Log(string v)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate { await LogAsync(v); })
            .FireAndForget(true);
    }

    internal async Task LogAsync(string v)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        OutputWindow.WriteLine(v);
    }
}



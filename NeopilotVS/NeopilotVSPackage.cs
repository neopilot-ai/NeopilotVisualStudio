global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using System.Collections.Generic;
using System.ComponentModel.Design;
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
    // default docking window, magic string for the guid of VSConstants.StandardToolWindows.SolutionExplorer
        // The GUID is hardcoded because attribute arguments must be compile-time constants.
        Window = "{3AE79031-E1BC-11D0-8F78-00A0C9110057}")] // default docking window, magic string for the
                                                   // guid of
                                                   // VSConstants.StandardToolWindows.SolutionExplorer
/// <summary>
/// This is the main class of the Neopilot Visual Studio extension.
/// It is responsible for initializing the extension, registering commands, and managing the language server.
/// </summary>
public sealed class NeopilotVSPackage : ToolkitPackage
{
    internal static NeopilotVSPackage? Instance { get; private set; }

    private NotificationInfoBar NotificationAuth;

    public OutputWindow OutputWindow;
    public SettingsPage SettingsPage;
    public LanguageServer LanguageServer;

    /// <summary>
    /// Initialization of the package; this method is called right after the package is sited, so this is the place
    /// where you can put all the initialization code that rely on services provided by VisualStudio.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
    /// <param name="progress">A provider for progress updates.</param>
    /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
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
        await LogAsync($"Neopilot Extension for Visual Studio v{Vsix.Version}");
    }

    /// <summary>
    /// Called when the package is being disposed.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        LanguageServer.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Ensures that the package is loaded. This is a blocking call.
    /// </summary>
    public static void EnsurePackageLoaded()
    {
        if (Instance != null) return;

        ThreadHelper.JoinableTaskFactory.Run(EnsurePackageLoadedAsync);
    }

    /// <summary>
    /// Ensures that the package is loaded asynchronously.
    /// </summary>
    public static async Task EnsurePackageLoadedAsync()
    {
        if (Instance != null) return;

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        IVsShell vsShell = (IVsShell)ServiceProvider.GlobalProvider.GetService(typeof(IVsShell)) ??
                           throw new NullReferenceException();

        Guid guidPackage = new(PackageGuids.NeopilotVSString);
        if (vsShell.IsPackageLoaded(ref guidPackage, out var _) == VSConstants.S_OK) return;

        if (vsShell.LoadPackage(ref guidPackage, out var _) != VSConstants.S_OK)
            throw new NullReferenceException("Failed to load Neopilot package.");
    }

    /// <summary>
    /// Updates the visibility of the sign-in and sign-out commands, and shows a notification to the user if they are not signed in.
    /// </summary>
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
        } else { 
	        await NotificationAuth.CloseAsync();
        }

        ChatToolWindow.Instance?.Reload();
    }

    /// <summary>
    /// Opens a URL in the default browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
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

    /// <summary>
    /// Gets the path to the AppData folder for the extension.
    /// </summary>
    public string GetAppDataPath() => Utilities.PathProvider.GetAppDataPath();

    /// <summary>
    /// Gets the path to the language server folder.
    /// </summary>
    public string GetLanguageServerFolder() => Utilities.PathProvider.GetLanguageServerFolder(LanguageServer.GetVersion());

    /// <summary>
    /// Gets the path to the language server executable.
    /// </summary>
    public string GetLanguageServerPath() => Utilities.PathProvider.GetLanguageServerPath(LanguageServer.GetVersion(), SettingsPage.EnterpriseMode);

    /// <summary>
    /// Gets the path to the database directory.
    /// </summary>
    public string GetDatabaseDirectory() => Utilities.PathProvider.GetDatabaseDirectory();

    /// <summary>
    /// Gets the path to the API key file.
    /// </summary>
    public string GetAPIKeyPath() => Utilities.PathProvider.GetAPIKeyPath();

    /// <summary>
    /// Gets a value indicating whether the user is signed in.
    /// </summary>
    public bool IsSignedIn() { return LanguageServer.GetKey().Length > 0; }

    /// <summary>
    /// Gets a value indicating whether the user has enterprise features enabled.
    /// </summary>
    public bool HasEnterprise() { return SettingsPage.EnterpriseMode; }

    /// <summary>
    /// Logs a message to the output window. This is a fire-and-forget method.
    /// </summary>
    /// <param name="v">The message to log.</param>
    internal void Log(string v)
    {
        LogAsync(v).FireAndForget(true);
    }

    /// <summary>
    /// Logs a message to the output window asynchronously.
    /// </summary>
    /// <param name="v">The message to log.</param>
    internal async Task LogAsync(string v)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        OutputWindow.WriteLine(v);
    }
}



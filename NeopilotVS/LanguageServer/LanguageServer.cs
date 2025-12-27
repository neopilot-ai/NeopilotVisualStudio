using NeopilotVS.Packets;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NeopilotVS.Utilities;

namespace NeopilotVS;

public class LanguageServer : IDisposable
{
    private string _languageServerURL;

    private int _port = 0;
    private System.Diagnostics.Process _process;
    
    private readonly Metadata _metadata;
    private readonly HttpClient _httpClient;
    private readonly NeopilotVSPackage _package;

    public readonly LanguageServerController Controller;
    private readonly LanguageServerInstaller _installer;
    private readonly WorkspaceIndexer _indexer;

    public LanguageServer()
    {
        _package = NeopilotVSPackage.Instance;
        _metadata = new();
        _httpClient = new HttpClient();
        Controller = new LanguageServerController();
        _installer = new LanguageServerInstaller(_package);
        _indexer = new WorkspaceIndexer(_package, this);
    }

    public async Task InitializeAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        string ideVersion = "17.0", locale = "en-US";

        try
        {
            locale = CultureInfo.CurrentUICulture.Name;
            Version? version = await VS.Shell.GetVsVersionAsync();
            if (version != null) ideVersion = version.ToString();
        }
        catch (Exception ex)
        {
            await _package.LogAsync($"Failed to get VS version or locale: {ex}");
        }

        // must be called before setting the metadata to retrieve _languageServerVersion first
        await _installer.PrepareAsync();

        _metadata.request_id = 0;
        string ideName = "visual_studio";
        if (_installer.Version == "1.16.0") { ideName = "vscode"; }
        _metadata.ide_name = ideName;
        _metadata.ide_version = ideVersion;
        _metadata.extension_name = Vsix.Name;
        _metadata.extension_version = _installer.Version;
        _metadata.session_id = Guid.NewGuid().ToString();
        _metadata.locale = locale;
        _metadata.disable_telemetry = false;
    }

    public void Dispose()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.Dispose();
                _process = null;
            }
        }
        catch (Exception ex)
        {
             _package.Log($"Dispose failed: {ex}");
        }

        Controller.Disconnect();
        _httpClient.Dispose();
    }

    public int GetPort() { return _port; }
    public string GetKey() { return _metadata.api_key; }
    public string GetVersion() { return _installer.Version; }
    public bool IsReady() { return _port != 0; }
    public async Task WaitReadyAsync()
    {
        while (!IsReady())
        {
            await Task.Delay(50);
        }
    }

    // Get API key from the authentication token
    public async Task SignInWithAuthTokenAsync(string authToken)
    {
        string url = _package.SettingsPage.EnterpriseMode
                         ? _package.SettingsPage.ApiUrl +
                                "/neo.seat_management_pb.SeatManagementService/RegisterUser"
                         : "https://api.neopilot.com/register_user/";

        RegisterUserRequest data = new() { firebase_id_token = authToken };
        RegisterUserResponse result = await RequestUrlAsync<RegisterUserResponse>(url, data);

        _metadata.api_key = result.api_key;

        if (_metadata.api_key == null)
        {
            await _package.LogAsync("Failed to sign in.");

            // show an error message box
            var msgboxResult = await VS.MessageBox.ShowAsync(
                "Neopilot: Failed to sign in. Please check the output window for more details.",
                "Do you want to retry?",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_RETRYCANCEL,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            if (msgboxResult == VSConstants.MessageBoxResult.IDRETRY)
                await SignInWithAuthTokenAsync(authToken);

            return;
        }

        File.WriteAllText(_package.GetAPIKeyPath(), _metadata.api_key);
        await _package.LogAsync("Signed in successfully");
        await _package.UpdateSignedInStateAsync();
    }

    // Open the browser to sign in
    public async Task SignInAsync()
    {
        // this will block until the sign in process has finished
        async Task < string ? > WaitForAuthTokenAsync()
        {
            // wait until we got the actual port of the LSP
            await WaitReadyAsync();

            GetAuthTokenResponse? result =
                await RequestCommandAsync<GetAuthTokenResponse>("GetAuthToken", new {});

            if (result == null)
            {
                // show an error message box
                var msgboxResult = await VS.MessageBox.ShowAsync(
                    "Neopilot: Failed to get the Authentication Token. Please check the output " +
                        "window for more details.",
                    "Do you want to retry?",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_RETRYCANCEL,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                return (msgboxResult == VSConstants.MessageBoxResult.IDRETRY)
                           ? await WaitForAuthTokenAsync()
                           : null;
            }

            return result.authToken;
        }

        string state = Guid.NewGuid().ToString();
        string portalUrl = _package.SettingsPage.EnterpriseMode ? _package.SettingsPage.PortalUrl
                                                                : "https://neopilot-ai.github.io";
        string redirectUrl = Uri.EscapeDataString($"http://127.0.0.1:{_port}/auth");
        string url =
            $"{portalUrl}/profile?response_type=token&redirect_uri={redirectUrl}&state={state}&scope=openid%20profile%20email&redirect_parameters_type=query";

        await _package.LogAsync("Opening browser to " + url);

        NeopilotVSPackage.OpenInBrowser(url);

        string authToken = await WaitForAuthTokenAsync();
        if (authToken != null) await SignInWithAuthTokenAsync(authToken);
    }

    // Delete the stored API key
    public async Task SignOutAsync()
    {
        _metadata.api_key = "";
        Utilities.FileUtilities.DeleteSafe(_package.GetAPIKeyPath());
        await _package.LogAsync("Signed out successfully");
        await _package.UpdateSignedInStateAsync();
    }

    public async Task OnInstallationCompleted()
    {
        await StartAsync();
    }

    public async Task StartAsync(bool ignoreDigitalSignature = true)
    {
        _port = 0;

        if (!ignoreDigitalSignature && !await _installer.VerifySignatureAsync()) return;

        string apiUrl = (_package.SettingsPage.ApiUrl.Equals("") ? "https://server.neopilot.com"
                                                                 : _package.SettingsPage.ApiUrl);
        string managerDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string databaseDir = _package.GetDatabaseDirectory();
        string languageServerPath = _package.GetLanguageServerPath();

        try
        {
            Directory.CreateDirectory(managerDir);
            Directory.CreateDirectory(databaseDir);
        }
        catch (Exception ex)
        {
            await _package.LogAsync(
                $"LanguageServer.StartAsync: Failed to create directories; Exception: {ex}");

            new NotificationInfoBar().Show(
                "[Neopilot] Critical error: Failed to create language server directories. Please " +
                    "check the output window for more details.",
                KnownMonikers.StatusError,
                true,
                null,
                NotificationInfoBar.SupportActions);
            return;
        }

        _process = new();
        _process.StartInfo.FileName = languageServerPath;
        _process.StartInfo.UseShellExecute = false;
        _process.StartInfo.CreateNoWindow = true;
        _process.StartInfo.RedirectStandardError = true;
        _process.EnableRaisingEvents = true;

        _process.StartInfo.Arguments =
            $"--api_server_url {apiUrl} --manager_dir \"{managerDir}\" --database_dir \"{databaseDir}\"" +
            $" --enable_chat_web_server --enable_chat_client --detect_proxy={_package.SettingsPage.EnableLanguageServerProxy}";

        if (_package.SettingsPage.EnableIndexing)
            _process.StartInfo.Arguments +=
                $" --enable_local_search --enable_index_service --search_max_workspace_file_count {_package.SettingsPage.IndexingMaxWorkspaceSize}";

        if (_package.SettingsPage.EnterpriseMode)
            _process.StartInfo.Arguments +=
                $" --enterprise_mode --portal_url {_package.SettingsPage.PortalUrl}";

        _process.ErrorDataReceived += LSP_OnPipeDataReceived;
        _process.OutputDataReceived += LSP_OnPipeDataReceived;
        _process.Exited += LSP_OnExited;

        await _package.LogAsync("Starting language server");

        try
        {
            _process.Start();
        }
        catch (Exception ex)
        {
            _process = null;
            await _package.LogAsync(
                $"LanguageServer.StartAsync: Failed to start the language server; Exception: {ex}");

            NotificationInfoBar errorBar = new();
            KeyValuePair<string, Action>[] actions = [
                new KeyValuePair<string, Action>("Retry",
                                                 delegate {
                                                     _process = null;
                                                     Utilities.FileUtilities.DeleteSafe(
                                                         languageServerPath);

                                                     ThreadHelper.JoinableTaskFactory
                                                         .RunAsync(async delegate {
                                                              await errorBar.CloseAsync();
                                                              await _installer.PrepareAsync();
                                                          })
                                                         .FireAndForget();
                                                 }),
            ];

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            errorBar.Show("[Neopilot] Critical Error: Failed to start the language server. Do " +
                               "you want to retry?",
                          KnownMonikers.StatusError,
                          true,
                          null,
                          [..actions, ..NotificationInfoBar.SupportActions]);

            return;
        }

        try
        {
            _process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            await _package.LogAsync(
                $"LanguageServer.StartAsync: BeginErrorReadLine failed; Exception: {ex}");

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            new NotificationInfoBar().Show("[Neopilot] Failed to read output from the language " +
                                               "server, Neopilot might not work properly.",
                                           KnownMonikers.IntellisenseWarning,
                                           true,
                                           null,
                                           NotificationInfoBar.SupportActions);

            var timeoutSec = 120;
            var elapsedSec = 0;

            while (elapsedSec++ < timeoutSec)
            {
                var files = Directory.GetFiles(managerDir);

                foreach (var file in files)
                {
                    if (int.TryParse(Path.GetFileName(file), out _port) && _port != 0) break;
                }

                if (_port != 0) break;

                await Task.Delay(1000);
            }

            if (_port != 0)
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(Controller.ConnectAsync)
                    .FireAndForget(true);
            }
            else
            {
                new NotificationInfoBar().Show(
                    "[Neopilot] Critical Error: Failed to get the language server port. Please " +
                        "check the output window for more details.",
                    KnownMonikers.StatusError,
                    true,
                    null,
                    NotificationInfoBar.SupportActions);

                return;
            }
        }

        if (!Utilities.ProcessExtensions.MakeProcessExitOnParentExit(_process))
        {
            await _package.LogAsync(
                "LanguageServer.StartAsync: MakeProcessExitOnParentExit failed");
        }

        string apiKeyFilePath = _package.GetAPIKeyPath();
        if (File.Exists(apiKeyFilePath)) { _metadata.api_key = File.ReadAllText(apiKeyFilePath); }

        await _package.UpdateSignedInStateAsync();
    }

    private void LSP_OnExited(object sender, EventArgs e)
    {
        _package.Log("Language Server Process exited unexpectedly, restarting...");

        _port = 0;
        _process = null;
        Controller.Disconnect();
        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate { await StartAsync(); })
            .FireAndForget(true);
    }

    private void LSP_OnPipeDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;

        Match match =
            Regex.Match(e.Data, @"Language server listening on (random|fixed) port at (\d{2,5})");

        if (match.Success)
        {
            if (int.TryParse(match.Groups[2].Value, out _port))
            {
                _package.Log($"Language server started on port {_port}");

                ChatToolWindow.Instance?.Reload();
                ThreadHelper.JoinableTaskFactory.RunAsync(Controller.ConnectAsync)
                    .FireAndForget(true);
            }
            else
            {
                _package.Log(
                    $"Error: Failed to parse the port number from \"{match.Groups[1].Value}\"");
            }
        }

        _package.Log("Language Server: " + e.Data);
    }

    private async Task<T?> RequestUrlAsync<T>(string url, object data,
                                               CancellationToken cancellationToken = default)
    {
        StringContent post_data =
            new(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
        try
        {
            HttpResponseMessage rq = await _httpClient.PostAsync(url, post_data, cancellationToken);
            if (rq.StatusCode == HttpStatusCode.OK)
            {
                return JsonConvert.DeserializeObject<T>(await rq.Content.ReadAsStringAsync());
            }

            if (rq.StatusCode == HttpStatusCode.Forbidden ||
                rq.StatusCode == HttpStatusCode.Unauthorized)
            {
                await _package.LogAsync("Session expired - please sign in again");
                await SignOutAsync();
            }
            else
            {
                await _package.LogAsync(
                    $"Error: Failed to send request to {url}, status code: {rq.StatusCode}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await _package.LogAsync(
                $"Error: Failed to send request to {url}, exception: {ex.Message}");
        }

        return default;
    }

    private async Task<T?> RequestCommandAsync<T>(string command, object data,
                                                   CancellationToken cancellationToken = default)
    {
        string url =
            $"http://127.0.0.1:{_port}/neo.language_server_pb.LanguageServerService/{command}";
        return await RequestUrlAsync<T>(url, data, cancellationToken);
    }

    public async Task<IList<CompletionItem>?>
    GetCompletionsAsync(string absolutePath, string text, Languages.LangInfo language,
                        int cursorPosition, string lineEnding, int tabSize, bool insertSpaces,
                        CancellationToken token)
    {
        if (!_indexer.IsInitialized) { await _indexer.InitializeTrackedWorkspaceAsync(); }
        var uri = new System.Uri(absolutePath);
        var absoluteUri = uri.AbsoluteUri;
        GetCompletionsRequest data =
            new() { metadata = GetMetadata(),
                    document = new() { text = text,
                                       editor_language = language.Name,
                                       language = language.Type,
                                       cursor_offset = (ulong)cursorPosition,
                                       line_ending = lineEnding,
                                       absolute_path = absolutePath,
                                       absolute_uri = absoluteUri,
                                       relative_path = Path.GetFileName(absolutePath) },
                    editor_options = new() {
                        tab_size = (ulong)tabSize,
                        insert_spaces = insertSpaces,
                        disable_autocomplete_in_comments =
                            !_package.SettingsPage.EnableCommentCompletion,
                    } };

        GetCompletionsResponse? result =
            await RequestCommandAsync<GetCompletionsResponse>("GetCompletions", data, token);
        return result != null ? result.completionItems : [];
    }

    public async Task AcceptCompletionAsync(string completionId)
    {
        AcceptCompletionRequest data =
            new() { metadata = GetMetadata(), completion_id = completionId };

        await RequestCommandAsync<AcceptCompletionResponse>("AcceptCompletion", data);
    }

    public async Task<GetProcessesResponse?> GetProcessesAsync()
    {
        return await RequestCommandAsync<GetProcessesResponse>("GetProcesses", new {});
    }

    public async Task<AddTrackedWorkspaceResponse?> AddTrackedWorkspaceAsync(string workspacePath)
    {
        AddTrackedWorkspaceRequest data = new() { workspace = workspacePath };
        return await RequestCommandAsync<AddTrackedWorkspaceResponse>("AddTrackedWorkspace", data);
    }

    public Metadata GetMetadata()
    {
        return new() { request_id = _metadata.request_id++,
                       api_key = _metadata.api_key,
                       ide_name = _metadata.ide_name,
                       ide_version = _metadata.ide_version,

                       extension_name = _metadata.extension_name,
                       extension_version = _metadata.extension_version,
                       session_id = _metadata.session_id,
                       locale = _metadata.locale,
                       disable_telemetry = _metadata.disable_telemetry };
    }

    public async Task<IList<FunctionInfo>?> GetFunctionsAsync(string absolutePath, string text,
                                                               Languages.LangInfo language,
                                                               int cursorPosition,
                                                               CancellationToken token)
    {
        if (!_indexer.IsInitialized) { await _indexer.InitializeTrackedWorkspaceAsync(); }
        var uri = new System.Uri(absolutePath);
        var absoluteUri = uri.AbsoluteUri;
        GetFunctionsRequest data = new() {
            document = new() { text = text.Replace("\r", ""),
                               editor_language = language.Name,
                               language = language.Type,
                               cursor_offset = (ulong)cursorPosition,
                               line_ending = "\n",
                               absolute_path = absolutePath,
                               absolute_uri = absoluteUri,
                               relative_path = Path.GetFileName(absolutePath) },
        };

        GetFunctionsResponse? result =
            await RequestCommandAsync<GetFunctionsResponse>("GetFunctions", data, token);
        return result != null ? result.FunctionCaptures : [];
    }

    public async Task<IList<ClassInfo>?> GetClassInfosAsync(string absolutePath, string text,
                                                             Languages.LangInfo language,
                                                             int cursorPosition, string lineEnding,
                                                             CancellationToken token)
    {
        if (!_indexer.IsInitialized) { await _indexer.InitializeTrackedWorkspaceAsync(); }
        var uri = new System.Uri(absolutePath);
        var absoluteUri = uri.AbsoluteUri;
        GetClassInfosRequest data = new() {
            document = new() { text = text.Replace("\r", ""),
                               editor_language = language.Name,
                               language = language.Type,
                               cursor_offset = (ulong)cursorPosition,
                               line_ending = "\n",
                               absolute_path = absolutePath,
                               absolute_uri = absoluteUri,
                               relative_path = Path.GetFileName(absolutePath) },
        };

        GetClassInfosResponse? result =
            await RequestCommandAsync<GetClassInfosResponse>("GetClassInfos", data, token);
        return result != null ? result.ClassCaptures : [];
    }
}

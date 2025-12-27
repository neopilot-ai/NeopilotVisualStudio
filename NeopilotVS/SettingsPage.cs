using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NeopilotVS;

[ComVisible(true)]
public class SettingsPage : DialogPage
{
    private bool enterpriseMode;
    private string portalUrl = "";
    private string apiUrl = "";
    private string extensionBaseUrl = "https://github.com/Neopilot-ai/neopilot/releases/download";
    private bool enableCommentCompletion = true;
    private bool enableLanguageServerProxy = false;
    private bool enableIndexing = true;
    private bool enableCodeLens = true;
    private int indexingMaxFileCount = 5000;
    private int indexingMaxProjectCount = 10;
    private string indexingFilesListPath = "";
    private bool indexOpenFiles = true;

    [Category("Neopilot")]
    [DisplayName("Self-Hosted Enterprise Mode")]
    [Description(
        "Set this to True if using Visual Studio with Neopilot Enterprise. Requires restart.")]
    public bool EnterpriseMode
    {
        get {
            return enterpriseMode;
        }
        set {
            enterpriseMode = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Portal Url")]
    [Description("URL of the Neopilot Enterprise Portal. Requires restart.")]
    public string PortalUrl
    {
        get {
            return portalUrl;
        }
        set {
            portalUrl = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Language Server Download URL")]
    [Description(
        "If you're experiencing network issues with GitHub and can't download the language " +
        "server, please change this to a GitHub Mirror URL instead. For example: " +
        "https://gh.api.99988866.xyz/https://github.com/Neopilot-ai/neopilot/releases/download")]
    public string ExtensionBaseUrl
    {
        get {
            return extensionBaseUrl;
        }
        set {
            extensionBaseUrl = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("API Url")]
    [Description("API Url for Neopilot Enterprise. Requires restart.")]
    public string ApiUrl
    {
        get {
            return apiUrl;
        }
        set {
            apiUrl = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Enable comment completion")]
    [Description("Whether or not Neopilot will provide completions for comments.")]
    public bool EnableCommentCompletion
    {
        get {
            return enableCommentCompletion;
        }
        set {
            enableCommentCompletion = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Enable Code Lens")]
    [Description("AI-powered inline action buttons in your editor. (Reload Required)")]
    public bool EnableCodeLens
    {
        get {
            return enableCodeLens;
        }
        set {
            enableCodeLens = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Enable language server proxy")]
    [Description("If you're experiencing network issues with the language server, we recommend " +
                 "enabling this option and using a VPN to resolve the issue. Requires restart.")]
    public bool EnableLanguageServerProxy
    {
        get {
            return enableLanguageServerProxy;
        }
        set {
            enableLanguageServerProxy = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Enable Neopilot Indexing")]
    [Description(
        "Allows Neopilot to index your current repository and provide better chat and " +
        "autocomplete responses based on relevant parts of your codebase. Requires restart.")]
    public bool EnableIndexing
    {
        get {
            return enableIndexing;
        }
        set {
            enableIndexing = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Indexing Max Workspace Size (File Count)")]
    [Description("If indexing is enabled, we will only attempt to index workspaces that have up " +
                 "to this many files. This file count ignores .gitignore and binary files.")]
    public int IndexingMaxWorkspaceSize
    {
        get {
            return indexingMaxFileCount;
        }
        set {
            indexingMaxFileCount = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Indexing Max Project Count")]
    [Description("The maximum number of distinct projects Neopilot will attempt to index in a " +
                 "single session. Requires restart.")]
    public int IndexingMaxProjectCount
    {
        get {
            return indexingMaxProjectCount;
        }
        set {
            indexingMaxProjectCount = value;
        }
    }

    [Category("Neopilot")]
    [DisplayName("Directories to Index List Path")]
    [Description("Absolute path to a .txt file that contains a line separated list of absolute " +
                 "paths of directories to index. Requires restart.")]
    public string IndexingFilesListPath
    {
        get {
            return indexingFilesListPath;
        }
        set {
            indexingFilesListPath = value;
        }
    }
    [Category("Neopilot")]
    [DisplayName("Index Open Files")]
    [Description("Neopilot will attempt to parse the project files that the files open upon IDE " +
                 "startup belong to. Requires restart.")]
    public bool IndexOpenFiles
    {
        get {
            return indexOpenFiles;
        }
        set {
            indexOpenFiles = value;
        }
    }
}

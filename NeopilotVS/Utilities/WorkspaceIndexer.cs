using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using NeopilotVS;

namespace NeopilotVS.Utilities;

public class WorkspaceIndexer
{
    private readonly NeopilotVSPackage _package;
    private readonly LanguageServer _server;

    public bool IsInitialized { get; private set; } = false;

    public WorkspaceIndexer(NeopilotVSPackage package, LanguageServer server)
    {
        _package = package;
        _server = server;
    }

    public async Task InitializeTrackedWorkspaceAsync()
    {
        if (IsInitialized) return;

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        DTE dte = (DTE)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
        
        // Safety check if DTE is null
        if (dte?.Solution == null) return;

        await _package.LogAsync($"Number of top-level projects: {dte.Solution.Projects.Count}");

        var documents = dte.Documents;
        var openFileProjects = new HashSet<EnvDTE.Project>();
        if (_package.SettingsPage.IndexOpenFiles)
        {
            if (documents != null)
            {
                foreach (EnvDTE.Document doc in documents)
                {
                    ProjectItem projectItem = doc.ProjectItem;
                    if (projectItem != null)
                    {
                        EnvDTE.Project project = projectItem.ContainingProject;
                        if (project != null && !openFileProjects.Contains(project))
                        {
                            openFileProjects.Add(project);
                        }
                    }
                }
            }
        }

        var inputDirectoriesToIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string projectListPath = _package.SettingsPage.IndexingFilesListPath.Trim();
        try
        {
            projectListPath = projectListPath.Trim();
            if (!string.IsNullOrEmpty(projectListPath) && File.Exists(projectListPath))
            {
                string[] lines = File.ReadAllLines(projectListPath);
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        if (Path.IsPathRooted(trimmedLine))
                        {
                            inputDirectoriesToIndex.Add(trimmedLine);
                        }
                    }
                }
                await _package.LogAsync($"Loaded from {inputDirectoriesToIndex.Count} directories");
            }
        }
        catch (Exception ex)
        {
            await _package.LogAsync($"Error reading project list: {ex.Message}");
        }

        List<string> projectsToIndex = new List<string>(inputDirectoriesToIndex);
        int maxToIndex = _package.SettingsPage.IndexingMaxProjectCount;
        projectsToIndex.AddRange(await GetDirectoriesToIndex(
            inputDirectoriesToIndex, openFileProjects, maxToIndex - projectsToIndex.Count, dte));
        await _package.LogAsync($"Number of projects to index: {projectsToIndex.Count}");

        for (int i = 0; i < Math.Min(maxToIndex, projectsToIndex.Count); i++)
        {
            try
            {
                await _package.LogAsync(
                    $"Processing Project {i + 1} of {projectsToIndex.Count}: {projectsToIndex[i]}");
                AddTrackedWorkspaceResponse response =
                    await _server.AddTrackedWorkspaceAsync(projectsToIndex[i]);
                if (response != null) { IsInitialized = true; }
            }
            catch (Exception ex)
            {
                await _package.LogAsync(
                    $"Error processing project {i + 1} of {projectsToIndex.Count}: {ex.Message}");
            }
        }
    }

    private async Task<List<string>> GetDirectoriesToIndex(HashSet<string> processedProjects,
                                                           HashSet<EnvDTE.Project> openFileProjects,
                                                           int remainingToFind, DTE dte)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        HashSet<string> remainingProjectsToIndexPath = new HashSet<string>();
        async Task AddFilesToIndexLists(EnvDTE.Project project)
        {
            if (remainingToFind <= 0) { return; }
            string projectFullName = project.FullName;
            await _package.LogAsync($"Adding files to index of project: {projectFullName}");
            if (!string.IsNullOrEmpty(projectFullName) &&
                !processedProjects.Any(p => projectFullName.StartsWith(p)))
            {
                string projectName = Path.GetFileNameWithoutExtension(projectFullName);
                IEnumerable<string> commonDirs = Enumerable.Empty<string>();
                string projectDir = Path.GetDirectoryName(projectFullName);
                // Parse the proj file to find all source directories
                if (File.Exists(projectFullName) &&
                    (projectFullName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                     projectFullName.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        XDocument projDoc = XDocument.Load(projectFullName);
                        IEnumerable<string> compileItems;
                        if (projectFullName.EndsWith(".vcxproj",
                                                     StringComparison.OrdinalIgnoreCase))
                        {
                            // Handle C++ project files
                            compileItems = projDoc.Descendants()
                                               .Where(x => x.Name.LocalName == "ClCompile" ||
                                                           x.Name.LocalName == "ClInclude")
                                               .Select(x => x.Attribute("Include")?.Value)
                                               .Where(x => !string.IsNullOrEmpty(x));
                        }
                        else
                        {
                            // Handle C# project files
                            compileItems = projDoc.Descendants()
                                               .Where(x => x.Name.LocalName == "Compile" ||
                                                           x.Name.LocalName == "Content")
                                               .Select(x => x.Attribute("Include")?.Value)
                                               .Where(x => !string.IsNullOrEmpty(x));
                        }

                        var fullPaths = new List<string>();
                        foreach (var item in compileItems)
                        {
                            try {
                                if (Path.IsPathRooted(item)) {
                                    fullPaths.Add(item);
                                } else {
                                    string fullPath = Path.GetFullPath(Path.Combine(projectDir, item));
                                    fullPaths.Add(fullPath);
                                }
                            } catch {}
                        }

                        commonDirs =
                            Utilities.FileUtilities.FindMinimumEncompassingDirectories(fullPaths);
                    }
                    catch (Exception ex)
                    {
                        await _package.LogAsync(
                            $"Failed to parse project file {projectFullName}: {ex.Message}");
                    }
                }

                if (commonDirs.Count() == 0) { commonDirs = new[] { projectDir }; }

                await _package.LogAsync(
                    $"Found set-covering directories for {projectName}: {commonDirs.Count()}");
                foreach (var dir in commonDirs)
                {
                    remainingToFind -= 1;
                    remainingProjectsToIndexPath.Add(dir);
                }

                processedProjects.Add(project.Name);
            }

            if (project.ProjectItems != null) {
                foreach (EnvDTE.ProjectItem item in project.ProjectItems)
                {
                    try
                    {
                        if (item.SubProject != null) { await AddFilesToIndexLists(item.SubProject); }
                    }
                    catch (Exception ex)
                    {
                        await _package.LogAsync($"Failed to process sub-project: {ex.Message}");
                        continue;
                    }
                }
            }
        }

        foreach (EnvDTE.Project project in openFileProjects)
        {
            try
            {
                await AddFilesToIndexLists(project);
            }
            catch (Exception ex)
            {
                await _package.LogAsync($"Failed to process open project: {ex.Message}");
                continue;
            }
        }
        foreach (EnvDTE.Project project in dte.Solution.Projects)
        {
            if (openFileProjects.Contains(project)) { continue; }
            try
            {
                await AddFilesToIndexLists(project);
            }
            catch (Exception ex)
            {
                await _package.LogAsync($"Failed to process remaining project: {ex.Message}");
                continue;
            }
            if (remainingToFind <= 0) { break; }
        }
        return remainingProjectsToIndexPath.ToList();
    }
}

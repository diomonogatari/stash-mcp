using Bitbucket.Net;
using ModelContextProtocol.Server;
using StashMcpServer.Services;
using System.ComponentModel;
using System.Text;

namespace StashMcpServer.Tools;

[McpServerToolType]
public class ProjectTools(
    ILogger<ProjectTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    BitbucketClient client,
    IServerSettings serverSettings)
    : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    [McpServerTool(Name = "list_projects"), Description("Lists all projects in the Bitbucket Server instance.")]
    public Task<string> ListProjectsAsync()
    {
        LogToolInvocation(nameof(ListProjectsAsync));

        var projects = CacheService.GetProjects().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

        if (projects.Count == 0)
        {
            return Task.FromResult("No projects found.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Projects ({projects.Count})");
        sb.AppendLine(new string('-', 40));

        foreach (var project in projects)
        {
            sb.AppendLine($"- {project.Name} [{project.Key}]");
        }

        return Task.FromResult(sb.ToString());
    }
}
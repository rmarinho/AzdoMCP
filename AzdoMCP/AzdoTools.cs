using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MCP.Services;

namespace AzdoMCP;

[McpServerToolType]
public static class AzdoTools
{
    [McpServerTool, Description("Get a list of build information from azure dev ops for a particular branch.")]
    public static async Task<string> GetBuilds(AzdoService azdoService, [Description("The name of the branch to get details for")] string branch)
    {
        var buildID = string.IsNullOrEmpty(branch) ? "refs/heads/main" : branch;
        var builds = await azdoService.GetBuildsByBranchNameAsync(buildID);
        return JsonSerializer.Serialize(builds);
    }

    // [McpServerTool, Description("Get a build log of azure dev ops build by id.")]
    // public static async Task<string> GetBuildLog(AzdoService azdoService, [Description("The id of the build to get details for")] string id)
    // {
    //      var buildID = string.IsNullOrEmpty(id) ? -1 : int.Parse(id);
    //     var build = await azdoService.GetBuildLogAsync(buildID);
    //     return JsonSerializer.Serialize(build);
    // }
}

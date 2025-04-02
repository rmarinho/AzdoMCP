using MCP.Services.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace MCP.Services;

public class AzdoService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzdoService> _logger;
    private readonly string _url;
    private readonly string _key;
    private readonly string _project;
    private readonly int _buildDefinitionId;
    private readonly string? _basePath;

    public AzdoService(IConfiguration configuration, ILogger<AzdoService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        foreach (var key in _configuration.AsEnumerable())
        {
            _logger.LogDebug("Configuration: {Key} = {Value}", key.Key, key.Value);
        }

        _url = _configuration["VSUrl"] ?? throw new InvalidOperationException("VSUrl must be set in configuration");
        _key = _configuration["VSKey"] ?? Environment.GetEnvironmentVariable("AZDO_PAT") ?? throw new InvalidOperationException("VSKey must be set in configuration");
        _project = _configuration["VSProject"] ?? throw new InvalidOperationException("VSProject must be set in configuration");

        if (!int.TryParse(_configuration["VSBuildDefinition"], out _buildDefinitionId))
        {
            throw new InvalidOperationException("VSBuildDefinition must be a valid integer");
        }

        _basePath = _configuration["BasePath"];
    }

    /// <summary>
    /// Creates a new Azure DevOps connection
    /// </summary>
    private async Task<BuildHttpClient> CreateBuildClientAsync(CancellationToken cancellationToken)
    {
        var connection = new VssConnection(new Uri(_url), new VssBasicCredential(string.Empty, _key));
        return await connection.GetClientAsync<BuildHttpClient>(cancellationToken);
    }

    /// <summary>
    /// Ensures a branch name starts with refs/heads/
    /// </summary>
    private static string NormalizeBranchName(string branchName) =>
        branchName.StartsWith("refs/heads/") ? branchName : $"refs/heads/{branchName}";

    /// <summary>
    /// Gets build logs for predefined branches and stores them to disk
    /// </summary>
    public async Task<Build[]> GetLogsAsync(int maxItems = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_basePath))
        {
            throw new InvalidOperationException("BasePath must be set in configuration.");
        }

        var badBasePath = Path.Combine(_basePath, "Bad");
        var goodBasePath = Path.Combine(_basePath, "Good");

        var branchName = "refs/heads/main";
        var badBranchName = "refs/heads/make-main-fail";

        var buildClient = await CreateBuildClientAsync(cancellationToken);

        var goodBuildsTask = GetBuildsWithLogs(_logger, _project, goodBasePath, _buildDefinitionId, branchName, buildClient, cancellationToken);
        var badBuildsTask = GetBuildsWithLogs(_logger, _project, badBasePath, _buildDefinitionId, badBranchName, buildClient, cancellationToken);

        await Task.WhenAll(goodBuildsTask, badBuildsTask);

        var goodBuilds = await goodBuildsTask;
        return goodBuilds?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets build information for a specific branch
    /// </summary>
    public async Task<BuildModel[]> GetBuildsByBranchNameAsync(string branchName, int maxItems = 10, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetBuildsByBranchNameAsync: {BranchName}", branchName);
        branchName = NormalizeBranchName(branchName);

        var buildClient = await CreateBuildClientAsync(cancellationToken);
        var branchBuilds = await GetBuildsWithoutLogs(_project, _buildDefinitionId, branchName, buildClient, maxItems, cancellationToken);

        if (branchBuilds == null || branchBuilds.Count == 0)
        {
            _logger.LogWarning("No builds found for branch {BranchName}", branchName);
            return [];
        }

        _logger.LogInformation("Builds found: {Count} for {branchName}", branchBuilds.Count, branchName);

        var buildModels = new List<BuildModel>();
        foreach (var build in branchBuilds.Take(1))
        {
            var buildReportTask = buildClient.GetBuildReportAsync(_project, build.Id, cancellationToken: cancellationToken);
            var buildTimeLineTask = buildClient.GetBuildTimelineAsync(_project, build.Id, cancellationToken: cancellationToken);

            await Task.WhenAll(buildReportTask, buildTimeLineTask);

            buildModels.Add(new BuildModel(_url, branchName, build.StartTime)
            {
                Build = build,
                Report = buildReportTask.Result,
                Timeline = buildTimeLineTask.Result
            });
        }

        return buildModels.ToArray();
    }

    /// <summary>
    /// Gets detailed build logs for a specific build ID
    /// </summary>
    public async Task<BuildLogModel[]> GetBuildLogAsync(int buildId, CancellationToken cancellationToken = default)
    {
        var buildClient = await CreateBuildClientAsync(cancellationToken);
        var buildTimeLine = await buildClient.GetBuildTimelineAsync(_project, buildId, cancellationToken: cancellationToken);

        var buildLogs = new List<BuildLogModel>();
        foreach (var timelineRecord in buildTimeLine.Records)
        {
            _logger.LogDebug("Record: {RecordType} {Name} {Id} {Attempt} PreviousAttempts {PreviousAttemptsCount}",
                timelineRecord.RecordType, timelineRecord.Name, timelineRecord.Id,
                timelineRecord.Attempt, timelineRecord.PreviousAttempts.Count);

            if (timelineRecord.RecordType != "Job" || timelineRecord.Log is null)
            {
                continue;
            }

            var logId = timelineRecord.Log.Id;
            var logStream = await buildClient.GetBuildLogAsync(_project, buildId, logId, cancellationToken: cancellationToken);

            using var reader = new StreamReader(logStream);
            string logContent = await reader.ReadToEndAsync(cancellationToken);

            buildLogs.Add(new BuildLogModel(_url, buildId, logId, timelineRecord.Log.Url, timelineRecord.Log.Type)
            {
                TimelineRecord = timelineRecord,
                ErrorCount = timelineRecord.ErrorCount,
                Attempt = timelineRecord.Attempt,
                TaskName = timelineRecord.Name,
                Result = timelineRecord.Result,
                Status = timelineRecord.State,
                LogContent = logContent
            });
        }

        return buildLogs.ToArray();
    }

    /// <summary>
    /// Saves build logs to disk
    /// </summary>
    async Task GetBuildLogs(string project, string basePath, int buildId, BuildHttpClient buildClient, CancellationToken cancellationToken)
    {
        var buildReport = await buildClient.GetBuildReportAsync(project, buildId, cancellationToken: cancellationToken);
        var buildTimeLine = await buildClient.GetBuildTimelineAsync(project, buildId, cancellationToken: cancellationToken);
        var pathToBuild = Path.Combine(basePath, buildId.ToString());

        foreach (var timelineRecord in buildTimeLine.Records)
        {
            await GetTimelineRecord(project, buildId, buildClient, pathToBuild, timelineRecord, cancellationToken);
        }
    }

    /// <summary>
    /// Processes a single timeline record and saves its logs
    /// </summary>
    async Task<bool> GetTimelineRecord(
        string project,
        int buildId,
        BuildHttpClient buildClient,
        string pathToBuild,
        TimelineRecord timelineRecord,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Record: {RecordType} {Name} {Id} {Attempt} PreviousAttempts {PreviousAttemptsCount}",
            timelineRecord.RecordType, timelineRecord.Name, timelineRecord.Id,
            timelineRecord.Attempt, timelineRecord.PreviousAttempts.Count);

        if (timelineRecord.RecordType != "Job")
        {
            return false;
        }

        Directory.CreateDirectory(pathToBuild);

        if (timelineRecord.PreviousAttempts.Count > 0)
        {
            // Save successful job log if available
            if (timelineRecord.Log is not null)
            {
                await SaveLogToFile(
                    project,
                    buildId,
                    pathToBuild,
                    timelineRecord.Log.Id,
                    buildClient,
                    $"{timelineRecord.Id}_good_{timelineRecord.Log.Id}_log.txt",
                    cancellationToken);
            }

            // Save logs for previous failed attempts
            _logger.LogDebug("Processing {Count} previous attempts for {Id}",
                timelineRecord.PreviousAttempts.Count, timelineRecord.Id);

            foreach (var previousAttempt in timelineRecord.PreviousAttempts)
            {
                await ProcessPreviousAttempt(
                    project,
                    buildId,
                    pathToBuild,
                    timelineRecord.Id,
                    previousAttempt,
                    buildClient,
                    cancellationToken);
            }
        }

        return true;
    }

    /// <summary>
    /// Process a previous build attempt and save its logs
    /// </summary>
    async Task ProcessPreviousAttempt(
        string project,
        int buildId,
        string pathToBuild,
        Guid timelineRecordId,
        TimelineAttempt previousAttempt,
        BuildHttpClient buildClient,
        CancellationToken cancellationToken)
    {
        var previousAttemptTimelineId = previousAttempt.TimelineId;
        var recordId = previousAttempt.RecordId;

        _logger.LogDebug("Previous Attempt: {TimelineId} {RecordId}", previousAttemptTimelineId, recordId);

        try
        {
            var previous = await buildClient.GetBuildTimelineAsync(
                project, buildId, previousAttemptTimelineId, cancellationToken: cancellationToken);

            var previousFailedRecord = previous.Records.FirstOrDefault(r => r.Id == recordId);

            if (previousFailedRecord?.Log is not null)
            {
                var fileName = $"{timelineRecordId}_failed_{previousFailedRecord.Attempt}_{previousFailedRecord.Log.Id}_log.txt";
                await SaveLogToFile(project, buildId, pathToBuild, previousFailedRecord.Log.Id, buildClient, fileName, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process previous attempt {TimelineId} {RecordId}",
                previousAttemptTimelineId, recordId);
            throw;
        }
    }

    /// <summary>
    /// Save a log file to disk
    /// </summary>
    private async Task SaveLogToFile(
        string project,
        int buildId,
        string pathToBuild,
        int logId,
        BuildHttpClient buildClient,
        string fileName,
        CancellationToken cancellationToken)
    {
        var logStream = await buildClient.GetBuildLogAsync(project, buildId, logId, cancellationToken: cancellationToken);

        using var reader = new StreamReader(logStream);
        string logContent = await reader.ReadToEndAsync(cancellationToken);

        var path = Path.Combine(pathToBuild, fileName);
        await File.WriteAllTextAsync(path, logContent, cancellationToken);
    }

    /// <summary>
    /// Gets builds and saves their logs to disk
    /// </summary>
    async Task<List<Build>?> GetBuildsWithLogs(
        ILogger<AzdoService> logger,
        string project,
        string basePath,
        int buildDefinitionId,
        string branchName,
        BuildHttpClient buildClient,
        CancellationToken cancellationToken)
    {
        List<Build>? buildsResult = null;

        try
        {
            var builds = await buildClient.GetBuildsAsync(
                project,
                definitions: [buildDefinitionId],
                statusFilter: BuildStatus.Completed,
                branchName: branchName,
                cancellationToken: cancellationToken);

            foreach (var build in builds)
            {
                logger.LogInformation("Get Build logs: {Id} {BuildNumber} {Status} {Result}",
                    build.Id, build.BuildNumber, build.Status, build.Result);

                await GetBuildLogs(project, basePath, build.Id, buildClient, cancellationToken);

                buildsResult ??= [];
                buildsResult.Add(build);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting builds for branch {BranchName}", branchName);
        }

        return buildsResult;
    }

    /// <summary>
    /// Gets builds without retrieving their logs
    /// </summary>
    async Task<List<Build>> GetBuildsWithoutLogs(
        string project,
        int buildDefinitionId,
        string branchName,
        BuildHttpClient buildClient,
        int maxItems = 10,
        CancellationToken cancellationToken = default)
    {
        List<Build> buildsResult = [];

        try
        {
            var builds = await buildClient.GetBuildsAsync(
                project,
                definitions: [buildDefinitionId],
                statusFilter: BuildStatus.Completed,
                branchName: branchName,
                top: maxItems,
                cancellationToken: cancellationToken);

            foreach (var build in builds)
            {
                _logger.LogInformation("Get Build: {Id} {BuildNumber} {Status} {Result}",
                    build.Id, build.BuildNumber, build.Status, build.Result);

                buildsResult.Add(build);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting builds for branch {BranchName}", branchName);
        }

        return buildsResult;
    }
}

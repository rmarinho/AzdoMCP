using Microsoft.TeamFoundation.Build.WebApi;
using System.Text.Json.Serialization;

namespace VigilantGuide.Services
{

    public enum BuildState
    {
        Completed,
        InProgress,
        Failed,
    }

    public record BuildModel(string Url, string BranchName, DateTime? StartedAt)
    {
        public Build? Build { get; set; }
        public BuildReportMetadata? Report { get; set; }
        public Timeline? Timeline { get; set; } = null;
    }

    public record BuildLogModel(string Url, int BuildId, int LogId, string LogUrl, string LogType)
    {
        [JsonIgnore]
        public string LogContent { get; set; } = "";
        public int? ErrorCount { get; internal set; }
        public int Attempt { get; internal set; }
        public string? TaskName { get; internal set; }
        public TaskResult? Result { get; internal set; }
        public TimelineRecord? TimelineRecord { get; internal set; }
        public TimelineRecordState? Status { get; internal set; }
    }
}

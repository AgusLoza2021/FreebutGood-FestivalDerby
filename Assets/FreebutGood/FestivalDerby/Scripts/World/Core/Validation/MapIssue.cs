namespace FestivalDerby.World
{
    /// <summary>One reported issue: which rule, how the editor should show it, and where on the path.</summary>
    public struct MapIssue
    {
        public MapIssueCode Code;
        public MapIssueSeverity Severity;
        public int Index;
        public float Distance;
        public string MessageKey;
    }
}

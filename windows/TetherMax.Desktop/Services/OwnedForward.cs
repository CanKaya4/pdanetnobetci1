namespace TetherMax;

internal enum ForwardState { Missing, Exact, Conflict }
internal static class OwnedForward {
    public static ForwardState Inspect(string listing,string serial,string? local,string remote) {
        if(string.IsNullOrWhiteSpace(serial)||local==null||!local.StartsWith("tcp:")||!int.TryParse(local.AsSpan(4),out int port)||port<1||port>65535)return ForwardState.Conflict;
        bool exact=false;
        foreach(string line in listing.Split('\n')) {
            var parts=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length!=3||parts[1]!=local)continue;
            if(parts[0]!=serial||parts[2]!="localabstract:"+remote)return ForwardState.Conflict;
            exact=true;
        }
        return exact?ForwardState.Exact:ForwardState.Missing;
    }
}

namespace TetherMax;
// Three consecutive relay-local failures trigger one recovery request. A good
// cellular sample resets the streak. No new DNS/HTTP probes are generated.
internal sealed class CellularAvailability {
 int failures;
 public void Reset()=>failures=0;
 public bool Observe(string? source){failures=source?.StartsWith("CELLULAR ",StringComparison.Ordinal)==true?0:failures+1;return failures==3;}
}

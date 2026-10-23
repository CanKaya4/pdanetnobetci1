internal static class RemovalVerifier {
 internal sealed record Result(bool InitiallyPresent,bool StillPresent,int Polls);
 // Windows PnP removal is asynchronous. Observe only; never delete unrelated devices.
 internal static Result Wait(Func<bool> exists,int maxPolls=40,Action? delay=null) {
  delay ??= ()=>Thread.Sleep(250);
  bool present=exists(),initial=present;int polls=0;
  while(present&&polls<maxPolls){delay();present=exists();polls++;}
  return new Result(initial,present,polls);
 }
}

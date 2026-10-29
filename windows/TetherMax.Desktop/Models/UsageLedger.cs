namespace TetherMax;

// Counts observed relay payload, never OS-wide usage or carrier billing.
internal sealed record UsageState(string Month="", long Used=0, long Budget=0,
    string Session="", ulong Up=0, ulong Down=0, ulong Sequence=0, string[]? Retired=null);

internal sealed class UsageLedger {
    public UsageState State {get;private set;}
    public UsageLedger(UsageState? state=null) {
        State=state??new();
        if(State.Used<0||State.Budget<0||State.Budget>1_000_000_000_000_000L||State.Session==null||State.Session.Length>128||State.Month==null||
            State.Retired?.Length>16||State.Retired?.Any(x=>x==null||x.Length>128)==true||
            State.Month!=""&&!DateTime.TryParseExact(State.Month+"-01","yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _))
            throw new System.IO.IOException("Veri sayacı kaydı geçersiz.");
    }
    public int Level=>State.Budget==0?0:State.Used>=State.Budget?100:State.Used>=State.Budget*.8?80:0;
    public void SetBudget(long bytes) {
        if(bytes<0||bytes>1_000_000_000_000_000L)throw new ArgumentOutOfRangeException(nameof(bytes));
        State=State with{Budget=bytes};
    }
    public bool Accept(string session,ulong sequence,ulong up,ulong down,DateTime utc) {
        if(string.IsNullOrWhiteSpace(session)||session.Length>128||sequence==0)return false;
        bool same=State.Session==session;
        if((State.Retired??[]).Contains(session)||same&&(sequence<=State.Sequence||up<State.Up||down<State.Down))return false;
        ulong du=same?up-State.Up:up,dd=same?down-State.Down:down;
        if(du>long.MaxValue||dd>long.MaxValue||du>(ulong)long.MaxValue-dd)return false;
        string month=utc.ToString("yyyy-MM",System.Globalization.CultureInfo.InvariantCulture);
        // Clock rollback never erases accumulated usage. Boundary interval is
        // assigned to the month in which its sample arrived.
        bool rollover=string.CompareOrdinal(month,State.Month)>0;
        long used=rollover?0:State.Used,delta=(long)(du+dd);
        var retired=State.Retired??[];
        if(!same&&State.Session!="")retired=retired.Append(State.Session).TakeLast(16).ToArray();
        State=State with{Month=rollover?month:State.Month,Used=delta>long.MaxValue-used?long.MaxValue:used+delta,
            Session=session,Up=up,Down=down,Sequence=sequence,Retired=retired};
        return true;
    }
}

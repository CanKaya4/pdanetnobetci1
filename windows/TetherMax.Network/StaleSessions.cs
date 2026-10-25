namespace TetherMax.Network;

public static class StaleSessions
{
    public static void Recover(IEnumerable<string> folders,Func<string,Journal> open,
        Func<int,long,bool> alive,Action<Journal,Plan> recover)
    {
        foreach(string folder in folders) {
            if(!File.Exists(Path.Combine(folder,"intent.json"))||File.Exists(Path.Combine(folder,"complete")))continue;
            var journal=open(folder);var plan=journal.Read();
            if(alive(plan.GuardianPid,plan.GuardianStart))throw new IOException("Etkin TetherMax oturumu var; önce bağlantıyı kesin.");
            recover(journal,plan);
            if(!journal.Completed)throw new IOException("Önceki TetherMax oturumunun kurtarması tamamlanmadı.");
        }
    }
}

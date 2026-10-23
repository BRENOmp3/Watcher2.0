namespace WatcherApp.Models;

public class WatcherConfig
{
    public int IntervaloCicloSegundos { get; set; } = 30;
    public string PastaLogs { get; set; } = "Logs";
    public int DiasRetencaoLogs { get; set; } = 30;
    public int MaximoTentativas { get; set; } = 3;
    public int SegundosEntreTentativas { get; set; } = 10;
    public List<SistemaMonitorado> Sistemas { get; set; } = new();
}
namespace WatcherApp.Models;

public class WatcherConfig
{
    public int IntervaloCicloSegundos { get; set; } = 30;
    public string PastaLogs { get; set; } = "Logs";
    public int DiasRetencaoLogs { get; set; } = 30;
    public int MaximoTentativas { get; set; } = 3;
    public int SegundosEntreTentativas { get; set; } = 10;
    public List<SistemaMonitorado> Sistemas { get; set; } = new();
    public EmailConfig Email { get; set; } = new();
    public int SegundosToleranciaServicoPendente { get; set; } = 60;
    public int MinutosEntreLembretesDeFalha { get; set; } = 30;
}
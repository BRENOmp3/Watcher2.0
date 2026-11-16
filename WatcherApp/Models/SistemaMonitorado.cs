namespace WatcherApp.Models;

public class SistemaMonitorado
{
    public string Nome { get; set; } = string.Empty;
    public string NomeServico { get; set; } = string.Empty;
    public string CaminhoLog { get; set; } = string.Empty;
    public int MinutosSemLogParaTravado { get; set; }
    public int SegundosCarenciaAposReinicio { get; set; }
}
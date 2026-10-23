using WatcherApp.Models;

namespace WatcherApp.Services;

public class LogMonitor
{
    private class EstadoLog
    {
        public long Tamanho { get; set; }
        public DateTime UltimaEscritaUtc { get; set; }
        public DateTime UltimaMudancaUtc { get; set; }
    }

    private readonly Dictionary<string, EstadoLog> _estados = new();
    private readonly Dictionary<string, DateTime> _carencias = new();

    public void IniciarCarencia(SistemaMonitorado sistema)
    {
        _carencias[sistema.Nome] = DateTime.UtcNow.AddSeconds(sistema.SegundosCarenciaAposReinicio);
        _estados.Remove(sistema.Nome);
    }

    public StatusLog Verificar(SistemaMonitorado sistema)
    {
        var agora = DateTime.UtcNow;

        if (_carencias.TryGetValue(sistema.Nome, out var fimCarencia))
        {
            if (agora < fimCarencia)
                return StatusLog.EmCarencia;

            _carencias.Remove(sistema.Nome);
        }

        var (tamanho, ultimaEscrita) = LerArquivo(sistema.CaminhoLog);

        if (!_estados.TryGetValue(sistema.Nome, out var estado))
        {
            _estados[sistema.Nome] = new EstadoLog
            {
                Tamanho = tamanho,
                UltimaEscritaUtc = ultimaEscrita,
                UltimaMudancaUtc = agora
            };
            return StatusLog.Ok;
        }

        if (tamanho != estado.Tamanho || ultimaEscrita != estado.UltimaEscritaUtc)
        {
            estado.Tamanho = tamanho;
            estado.UltimaEscritaUtc = ultimaEscrita;
            estado.UltimaMudancaUtc = agora;
            return StatusLog.Ok;
        }

        var semMudanca = agora - estado.UltimaMudancaUtc;
        var limite = TimeSpan.FromMinutes(sistema.MinutosSemLogParaTravado);

        return semMudanca >= limite ? StatusLog.Travado : StatusLog.Ok;
    }

    private static (long Tamanho, DateTime UltimaEscritaUtc) LerArquivo(string caminho)
    {
        var info = new FileInfo(caminho);

        return info.Exists
            ? (info.Length, info.LastWriteTimeUtc)
            : (-1, DateTime.MinValue);
    }
}
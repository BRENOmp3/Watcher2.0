using System.Collections.Concurrent;
using WatcherApp.Models;

namespace WatcherApp.Services;

public class LogMonitor
{
    // Foto do log num instante. Por ser um record, nunca é alterado:
    // quando o log muda, criamos uma foto nova e trocamos a antiga.
    private sealed record EstadoLog(
        long Tamanho,
        DateTime UltimaEscritaUtc,
        DateTime UltimaMudancaUtc);

    private readonly ConcurrentDictionary<string, EstadoLog> _estados = new();
    private readonly ConcurrentDictionary<string, DateTime> _carencias = new();
    private readonly ConcurrentDictionary<string, DateTime> _semArquivoDesde = new();

    public void IniciarCarencia(SistemaMonitorado sistema)
    {
        _carencias[sistema.Nome] =
            DateTime.UtcNow.AddSeconds(sistema.SegundosCarenciaAposReinicio);

        _estados.TryRemove(sistema.Nome, out _);
        _semArquivoDesde.TryRemove(sistema.Nome, out _);
    }

    public StatusLog Verificar(SistemaMonitorado sistema)
    {
        var agora = DateTime.UtcNow;
        var limite = TimeSpan.FromMinutes(sistema.MinutosSemLogParaTravado);

        if (_carencias.TryGetValue(sistema.Nome, out var fimCarencia))
        {
            if (agora < fimCarencia)
                return StatusLog.EmCarencia;

            _carencias.TryRemove(sistema.Nome, out _);
        }

        var arquivo = LocalizarArquivo(sistema.CaminhoLog);

        if(arquivo is null)
            return VerificarArquivoAusente(sistema,agora, limite);

        _semArquivoDesde.TryRemove(sistema.Nome, out _);

        var tamanho = arquivo.Length;
        var ultimaEscrita = arquivo.LastWriteTimeUtc;

        if (!_estados.TryGetValue(sistema.Nome, out var estado))
        {
            _estados[sistema.Nome] = new EstadoLog(tamanho, ultimaEscrita, agora);
            return StatusLog.Ok;
        }

        if (tamanho != estado.Tamanho || ultimaEscrita != estado.UltimaEscritaUtc)
        {
            _estados[sistema.Nome] = new EstadoLog(tamanho, ultimaEscrita, agora);
            return StatusLog.Ok;
        }

        return agora - estado.UltimaMudancaUtc >= limite
            ? StatusLog.Travado
            : StatusLog.Ok;
    }

    // Aceita um caminho fixo ou um padrão com * no nome do arquivo.
    // Com padrão, devolve o arquivo modificado mais recentemente. Devolve null se não houver nenhum.
    public static FileInfo? LocalizarArquivo(string caminhoLog)
    {
        var nome = Path.GetFileName(caminhoLog);

        if (!nome.Contains('*') && !nome.Contains('?'))
        {
            var fixo = new FileInfo(caminhoLog);
            return fixo.Exists ? fixo : null;
        }

        var pasta = Path.GetDirectoryName(caminhoLog);

        if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta))
            return null;

        return new DirectoryInfo(pasta)
            .EnumerateFiles(nome)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private StatusLog VerificarArquivoAusente(
        SistemaMonitorado sistema,
        DateTime agora,
        TimeSpan limite)
    {
        // GetOrAdd guarda o momento em que vimos a falta pela primeira vez.
        var desde = _semArquivoDesde.GetOrAdd(sistema.Nome, agora);

        return agora - desde >= limite
            ? StatusLog.LogNaoEncontrado
            : StatusLog.AguardandoLog;
    }
}
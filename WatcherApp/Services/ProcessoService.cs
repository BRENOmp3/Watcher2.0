using System.Diagnostics;

namespace WatcherApp.Services;

public class ProcessoService
{
    public bool EstaRodando(string nomeProcesso)
    {
        var processos = Process.GetProcessesByName(nomeProcesso);
        try
        {
            return processos.Length > 0;
        }
        finally
        {
            foreach (var p in processos) p.Dispose();
        }
    }

    public void Iniciar(string caminhoExecutavel)
    {
        if (!File.Exists(caminhoExecutavel))
            throw new FileNotFoundException($"Executável não encontrado: {caminhoExecutavel}");

        var info = new ProcessStartInfo
        {
            FileName = caminhoExecutavel,
            WorkingDirectory = Path.GetDirectoryName(caminhoExecutavel)!,
            UseShellExecute = false
        };

        Process.Start(info)?.Dispose();
    }

    public void Encerrar(string nomeProcesso, int timeoutSegundos = 15)
    {
        var processos = Process.GetProcessesByName(nomeProcesso);

        foreach (var p in processos)
        {
            try
            {
                p.Kill(entireProcessTree: true);

                if (!p.WaitForExit(timeoutSegundos * 1000))
                    throw new InvalidOperationException(
                        $"O processo '{nomeProcesso}' não encerrou em {timeoutSegundos}s.");
            }
            finally
            {
                p.Dispose();
            }
        }
    }
}
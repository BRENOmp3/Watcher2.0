using System.Collections.Concurrent;

namespace WatcherApp.Services;


// Lembra quais problemas já foram alertados, para alertar só na primeira vez,
// e diz quando é hora de lembrar que um problema continua.

public class MemoriaDeAlertas
{
    // Desde quando o problema existe e quando foi o último aviso (alerta ou lembrete).
    private sealed record Registro(DateTime Desde, DateTime UltimoAviso);

    private readonly ConcurrentDictionary<string, Registro> _ativos = new();
    private readonly TimeSpan _intervaloLembrete;

    public MemoriaDeAlertas(TimeSpan intervaloLembrete)
    {
        _intervaloLembrete = intervaloLembrete;
    }

    // Devolve true só na PRIMEIRA vez que o problema aparece (hora de alertar).
    public bool Registrar(string sistema, string problema)
    {
        var agora = DateTime.UtcNow;
        return _ativos.TryAdd(Chave(sistema, problema), new Registro(agora, agora));
    }

    // Devolve true se o problema estava ativo e agora foi dado como resolvido.
    public bool Resolver(string sistema, string problema) =>
        _ativos.TryRemove(Chave(sistema, problema), out _);

    // Devolve true se o problema está ativo e já passou o intervalo desde o último aviso.
    // Ao devolver true, já marca "agora" como o último aviso.
    public bool DeveLembrar(string sistema, string problema, out TimeSpan duracao)
    {
        duracao = TimeSpan.Zero;

        if (_intervaloLembrete <= TimeSpan.Zero)
            return false; // lembretes desligados

        var chave = Chave(sistema, problema);

        if (!_ativos.TryGetValue(chave, out var registro))
            return false;

        var agora = DateTime.UtcNow;

        if (agora - registro.UltimoAviso < _intervaloLembrete)
            return false;

        // TryUpdate só troca se ninguém mexeu no registro nesse meio tempo.
        // Se outra thread chegou primeiro, ela é quem lembra: sem lembrete duplicado.
        var atualizado = registro with { UltimoAviso = agora };

        if (!_ativos.TryUpdate(chave, atualizado, registro))
            return false;

        duracao = agora - registro.Desde;
        return true;
    }

    private static string Chave(string sistema, string problema) =>
        $"{sistema}|{problema}";
}
using WatcherApp.Models;
using System.Net.Mail;

namespace WatcherApp.Services;

public static class ConfigValidator
{
    public static List<string> Validar(WatcherConfig config)
    {
        var erros = new List<string>();

        if (config.IntervaloCicloSegundos <= 0)
            erros.Add("'IntervaloCicloSegundos' deve ser maior que zero.");

        if (config.MaximoTentativas < 1)
            erros.Add("'MaximoTentativas' deve ser pelo menos 1.");

        if (config.SegundosEntreTentativas < 0)
            erros.Add("'SegundosEntreTentativas' não pode ser negativo.");

        if (config.SegundosToleranciaServicoPendente < 0)
            erros.Add("'SegundosToleranciaServicoPendente' não pode ser negativo.");

        if (string.IsNullOrWhiteSpace(config.PastaLogs))
            erros.Add("'PastaLogs' não pode ser vazio.");

        if (config.MinutosEntreLembretesDeFalha < 0)
            erros.Add("'MinutosEntreLembretesDeFalha' não pode ser negativo (use 0 para desligar).");

        if (config.Email.Habilitado)
        {
            var email = config.Email;

            if (string.IsNullOrWhiteSpace(email.ServidorSmtp))
                erros.Add("'Email.ServidorSmtp' é obrigatório quando o email está habilitado.");

            if (email.PortaSmtp is < 1 or > 65535)
                erros.Add("'Email.PortaSmtp' deve estar entre 1 e 65535.");

            if (!EmailValido(email.Remetente))
                erros.Add("'Email.Remetente' não é um endereço de email válido.");

            if (!email.Destinatarios.Any() || email.Destinatarios.Any(d => !EmailValido(d)))
                erros.Add("'Email.Destinatarios' precisa ter ao menos um email, e todos devem ser válidos.");

            if (email.UsarCredenciais && string.IsNullOrWhiteSpace(email.Usuario))
                erros.Add("'Email.Usuario' é obrigatório quando 'UsarCredenciais' está ligado.");

            if (email.UsarCredenciais && string.IsNullOrWhiteSpace(email.VariavelAmbienteSenha))
                erros.Add("'Email.VariavelAmbienteSenha' é obrigatório quando 'UsarCredenciais' está ligado.");
        }

        if (config.Sistemas.Count == 0)
        {
            erros.Add("Nenhum sistema configurado em 'Sistemas'.");
            return erros;
        }

        for (var i = 0; i < config.Sistemas.Count; i++)
            ValidarSistema(config.Sistemas[i], i + 1, erros);

        AdicionarDuplicados(config.Sistemas.Select(s => s.Nome), "Nome", erros);
        AdicionarDuplicados(config.Sistemas.Select(s => s.NomeServico), "NomeServico", erros);

        return erros;

    }

    private static void ValidarSistema(SistemaMonitorado sistema, int posicao, List<string> erros)
    {
        var id = string.IsNullOrWhiteSpace(sistema.Nome)
            ? $"Sistema #{posicao}"
            : $"Sistema #{posicao} ('{sistema.Nome}')";

        if (string.IsNullOrWhiteSpace(sistema.Nome))
            erros.Add($"{id}: 'Nome' não pode ser vazio.");

        if (string.IsNullOrWhiteSpace(sistema.NomeServico))
            erros.Add($"{id}: 'NomeServico' não pode ser vazio.");
        if (string.IsNullOrWhiteSpace(sistema.CaminhoLog))
            erros.Add($"{id}: 'CaminhoLog' não pode ser vazio.");

        if (sistema.MinutosSemLogParaTravado <= 0)
            erros.Add($"{id}: 'MinutosSemLogParaTravado' deve ser maior que zero.");

        if (sistema.SegundosCarenciaAposReinicio < 0)
            erros.Add($"{id}: 'SegundosCarenciaAposReinicio' não pode ser negativo.");
    }

    private static void AdicionarDuplicados(IEnumerable<string> valores, string campo, List<string> erros)
    {
        var duplicados = valores
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(grupo => grupo.Count() > 1)
            .Select(grupo => grupo.Key);

        foreach (var valor in duplicados)
            erros.Add($"'{campo}' repetido entre sistemas: '{valor}'. Cada sistema precisa de um valor único.");
    }

    private static bool EmailValido(string? valor) =>
    !string.IsNullOrWhiteSpace(valor) && MailAddress.TryCreate(valor, out _);
}
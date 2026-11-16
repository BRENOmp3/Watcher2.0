using System.Text.Json;
using WatcherApp.Models;

namespace WatcherApp.Services;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, // permite comentários // e /* */
        AllowTrailingCommas = true                      // tolera uma vírgula sobrando no fim de uma lista
    };
    public static WatcherConfig Carregar(string nomeArquivo = "config.json")
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, nomeArquivo);

        if (!File.Exists(caminho))
            throw new FileNotFoundException($"Arquivo de configuração não encontrado: {caminho}");

        var json = File.ReadAllText(caminho);

        WatcherConfig? config;

        try
        {
            config = JsonSerializer.Deserialize<WatcherConfig>(json, Opcoes);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"O arquivo '{nomeArquivo}' não é um JSON válido (vírgula faltando? aspas? chaves?): {ex.Message}");
        }

        if (config is null)
            throw new InvalidOperationException($"O arquivo '{nomeArquivo}' está vazio.");

        var erros = ConfigValidator.Validar(config);

        if (erros.Count > 0)
        {
            var lista = string.Join(Environment.NewLine, erros.Select(e => $" - {e}"));
            throw new InvalidOperationException($"Configuração inválida:{Environment.NewLine}{lista}");
        }

        return config;
    }
}
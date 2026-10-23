using System.Text.Json;
using WatcherApp.Models;

namespace WatcherApp.Services;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static WatcherConfig Carregar(string nomeArquivo = "config.json")
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, nomeArquivo);

        if (!File.Exists(caminho))
            throw new FileNotFoundException($"Arquivo de configuração não encontrado: {caminho}");

        var json = File.ReadAllText(caminho);

        var config = JsonSerializer.Deserialize<WatcherConfig>(json, Opcoes)
            ?? throw new InvalidOperationException("O arquivo de configuração está vazio ou inválido.");

        if (config.Sistemas.Count == 0)
            throw new InvalidOperationException("Nenhum sistema configurado para monitoramento.");

        return config;
    }
}
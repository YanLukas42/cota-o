using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

class AppConfig
{
    public string? EmailDestino { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public string? SmtpUser { get; set; }
    public string? SmtpPass { get; set; }

    public static AppConfig Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo de configuração não encontrado.", filePath);

        string json = File.ReadAllText(filePath);
        var config = JsonSerializer.Deserialize<AppConfig>(json);
        if (config == null)
            throw new InvalidOperationException("Falha ao desserializar o arquivo de configuração.");
        return config;
    }
}

class Program
{
    private const string AlphaVantageApiKey = "7503W0APL9GEMBGH";

    static async Task Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Uso: stock-quote-alert.exe ATIVO PRECO_VENDA PRECO_COMPRA");
            return;
        }

        string ativo = args[0];
        if (!decimal.TryParse(args[1], out decimal precoVenda))
        {
            Console.WriteLine("Preço de venda inválido.");
            return;
        }
        if (!decimal.TryParse(args[2], out decimal precoCompra))
        {
            Console.WriteLine("Preço de compra inválido.");
            return;
        }

        AppConfig config = AppConfig.Load("appsettings.json");
        Console.WriteLine($"Monitorando ativo {ativo} (Venda: {precoVenda}, Compra: {precoCompra})...");

        using var http = new HttpClient();

        while (true)
        {
            decimal precoAtual = await ObterCotacao(http, ativo);

            Console.WriteLine($"{DateTime.Now}: {ativo} = {precoAtual}");

            if (precoAtual >= precoVenda)
            {
                EnviarEmail(config, ativo, precoAtual, "subiu acima do preço de VENDA");
            }
            else if (precoAtual <= precoCompra)
            {
                EnviarEmail(config, ativo, precoAtual, "caiu abaixo do preço de COMPRA");
            }

            Thread.Sleep(60000); 
        }
    }

    static async Task<decimal> ObterCotacao(HttpClient http, string ativo)
    {
        try
        {
            string url = $"https://www.alphavantage.co/query?function=GLOBAL_QUOTE&symbol={ativo}.SA&apikey={AlphaVantageApiKey}";
            var response = await http.GetStringAsync(url);

            JsonNode? json = JsonNode.Parse(response);
            if (json == null)
                throw new Exception("Resposta da API não pôde ser convertida para JSON.");

            string? precoStr = json["Global Quote"]?["05. price"]?.ToString();

            if (string.IsNullOrWhiteSpace(precoStr))
                throw new Exception("Não foi possível obter o preço: valor nulo ou vazio.");

            if (decimal.TryParse(precoStr, out decimal preco))
                return preco;

            throw new Exception("Não foi possível obter o preço.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao consultar API: {ex.Message}");
            return -1;
        }
    }

    static void EnviarEmail(AppConfig config, string ativo, decimal preco, string mensagem)
    {
        if (string.IsNullOrWhiteSpace(config.SmtpPass))
        {
            Console.WriteLine("Senha SMTP não configurada. Preencha no appsettings.json antes de enviar emails.");
            return;
        }

        try
        {
            using (var client = new SmtpClient(config.SmtpHost, config.SmtpPort))
            {
                client.Credentials = new NetworkCredential(config.SmtpUser, config.SmtpPass);
                client.EnableSsl = true;

                MailMessage mail = new MailMessage();
                if (string.IsNullOrWhiteSpace(config.SmtpUser))
                {
                    Console.WriteLine("Usuário SMTP não configurado. Preencha no appsettings.json antes de enviar emails.");
                    return;
                }
                mail.From = new MailAddress(config.SmtpUser);
                if (string.IsNullOrWhiteSpace(config.EmailDestino))
                {
                    Console.WriteLine("Email de destino não configurado. Preencha no appsettings.json antes de enviar emails.");
                    return;
                }
                mail.To.Add(config.EmailDestino);
                mail.Subject = $"Alerta de Cotação - {ativo}";
                mail.Body = $"O ativo {ativo} {mensagem}. Cotação atual: {preco}";

                client.Send(mail);
            }
            Console.WriteLine($"[ALERTA ENVIADO] {ativo} {mensagem}. Cotação: {preco}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao enviar email: {ex.Message}");
        }
    }
}

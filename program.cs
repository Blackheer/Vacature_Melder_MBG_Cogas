using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Net;
using Newtonsoft.Json;
using Microsoft.Playwright;

class Program
{
    private static readonly string Url = "https://werkenbij.cogas.nl/vacatures?functiegroep=";
    private static readonly string JsonFile = "vacancies.json";

    public static async Task Main(string[] args)
    {
        Console.WriteLine("Start vacature-controle via Playwright...");

        var currentVacancies = await GetCurrentVacanciesAsync();
        
        if (!File.Exists(JsonFile))
        {
            File.WriteAllText(JsonFile, "[]");
        }

        if (currentVacancies.Count == 0)
        {
            Console.WriteLine("Geen vacatures gevonden op de pagina.");
            return;
        }

        List<JobVacancy> oldVacancies = new List<JobVacancy>();
        if (File.Exists(JsonFile))
        {
            try
            {
                string jsonContent = File.ReadAllText(JsonFile);
                oldVacancies = JsonConvert.DeserializeObject<List<JobVacancy>>(jsonContent) ?? new List<JobVacancy>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij lezen JSON-bestand: {ex.Message}");
            }
        }

        var oldLinks = new HashSet<string>(oldVacancies.Select(v => v.Link));
        var newVacancies = currentVacancies.Where(v => !oldLinks.Contains(v.Link)).ToList();

        if (newVacancies.Count > 0)
        {
            Console.WriteLine($"Er zijn {newVacancies.Count} nieuwe vacatures gevonden!");
            SendNotification(newVacancies);
        }
        else
        {
            Console.WriteLine("Geen nieuwe vacatures gevonden.");
        }

        string updatedJson = JsonConvert.SerializeObject(currentVacancies, Formatting.Indented);
        File.WriteAllText(JsonFile, updatedJson);
    }

    private static async Task<List<JobVacancy>> GetCurrentVacanciesAsync()
    {
        var vacancies = new List<JobVacancy>();
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();

            await page.GotoAsync(Url);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var elements = await page.QuerySelectorAllAsync("a[href*='/vacature/']");

            foreach (var element in elements)
            {
                string href = await element.GetAttributeAsync("href") ?? string.Empty;
                string title = (await element.InnerTextAsync())?.Trim() ?? string.Empty;

                if (!string.IsNullOrEmpty(title) && title.Length > 3)
                {
                    if (href.StartsWith("/"))
                    {
                        href = "https://werkenbij.cogas.nl" + href;
                    }

                    var vacancy = new JobVacancy { Title = title, Link = href };
                    if (!vacancies.Any(v => v.Link == vacancy.Link))
                    {
                        vacancies.Add(vacancy);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fout bij ophalen via Playwright: {ex.Message}");
        }

        return vacancies;
    }

    private static void SendNotification(List<JobVacancy> newVacancies)
    {
        string? senderEmail = Environment.GetEnvironmentVariable("MAIL_USERNAME");
        string? senderPassword = Environment.GetEnvironmentVariable("MAIL_PASSWORD");
        string? receiverEmail = Environment.GetEnvironmentVariable("MAIL_TO");

        if (string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(senderPassword) || string.IsNullOrEmpty(receiverEmail))
        {
            Console.WriteLine("E-mailconfiguratie ontbreekt in omgevingsvariabelen.");
            return;
        }

        try
        {
            var mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(senderEmail);
            mailMessage.To.Add(receiverEmail);
            mailMessage.Subject = $"🚨 Nieuwe Cogas vacature(s) gevonden! ({newVacancies.Count})";

            string body = "Er zijn nieuwe vacatures gevonden bij Cogas:\n\n";
            foreach (var v in newVacancies)
            {
                body += $"- {v.Title}\n  {v.Link}\n\n";
            }
            mailMessage.Body = body;

            using (var smtpClient = new SmtpClient("smtp.gmail.com", 587))
            {
                smtpClient.Port = 587;
                smtpClient.Credentials = new NetworkCredential(senderEmail, senderPassword);
                smtpClient.EnableSsl = true;
                smtpClient.Send(mailMessage);
            }

            Console.WriteLine("E-mail succesvol verzonden!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fout bij verzenden e-mail: {ex.Message}");
        }
    }
}

public class JobVacancy
{
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("link")]
    public string Link { get; set; } = string.Empty;
}

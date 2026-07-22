using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // Haal huidige vacatures op van de website
        var currentVacancies = await GetCurrentVacanciesAsync();

        if (currentVacancies.Count == 0)
        {
            Console.WriteLine("Geen vacatures gevonden op de pagina. Afbreken.");
            return;
        }

        Console.WriteLine($"{currentVacancies.Count} vacature(s) gevonden op de pagina.");

        // Lees eerder opgeslagen vacatures uit JSON
        var oldVacancies = new List<JobVacancy>();
        if (File.Exists(JsonFile))
        {
            try
            {
                string jsonContent = File.ReadAllText(JsonFile);
                oldVacancies = JsonConvert.DeserializeObject<List<JobVacancy>>(jsonContent)
                               ?? new List<JobVacancy>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij lezen JSON-bestand: {ex.Message}");
            }
        }
        else
        {
            // Eerste keer: schrijf leeg bestand aan zodat git het kan tracken
            File.WriteAllText(JsonFile, "[]");
        }

        // Vergelijk op basis van URL
        var oldLinks = new HashSet<string>(oldVacancies.Select(v => v.Link));
        var newVacancies = currentVacancies.Where(v => !oldLinks.Contains(v.Link)).ToList();

        if (newVacancies.Count > 0)
        {
            Console.WriteLine($"{newVacancies.Count} nieuwe vacature(s) gevonden!");
            SendEmailNotification(newVacancies);
        }
        else
        {
            Console.WriteLine("Geen nieuwe vacatures gevonden.");
        }

        // Sla de huidige stand op (overschrijft het oude bestand)
        string updatedJson = JsonConvert.SerializeObject(currentVacancies, Formatting.Indented);
        File.WriteAllText(JsonFile, updatedJson);
        Console.WriteLine("vacancies.json bijgewerkt.");
    }

    private static async Task<List<JobVacancy>> GetCurrentVacanciesAsync()
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            var page = await browser.NewPageAsync();

            // Navigeer met een timeout van 30 seconden
            await page.GotoAsync(Url, new PageGotoOptions
            {
                Timeout = 30_000,
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Selecteer alle vacature-links
            var elements = await page.QuerySelectorAllAsync("a[href*='/vacature/']");

            foreach (var element in elements)
            {
                string href = await element.GetAttributeAsync("href") ?? string.Empty;
                string title = (await element.InnerTextAsync())?.Trim() ?? string.Empty;

                // Filter lege of te korte teksten
                if (string.IsNullOrEmpty(title) || title.Length <= 3)
                    continue;

                // Maak relatieve URLs absoluut
                if (href.StartsWith("/"))
                    href = "https://werkenbij.cogas.nl" + href;

                // Voorkom duplicaten op basis van URL
                if (!vacancies.Any(v => v.Link == href))
                {
                    vacancies.Add(new JobVacancy { Title = title, Link = href });
                    Console.WriteLine($"  Gevonden: {title}");
                }
            }
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Time-out bij laden van de vacaturepagina.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fout bij ophalen via Playwright: {ex.Message}");
        }

        return vacancies;
    }

    private static void SendEmailNotification(List<JobVacancy> newVacancies)
    {
        string? senderEmail   = Environment.GetEnvironmentVariable("MAIL_USERNAME");
        string? senderPassword = Environment.GetEnvironmentVariable("MAIL_PASSWORD");
        string? receiverEmail  = Environment.GetEnvironmentVariable("MAIL_TO");

        if (string.IsNullOrEmpty(senderEmail) ||
            string.IsNullOrEmpty(senderPassword) ||
            string.IsNullOrEmpty(receiverEmail))
        {
            Console.WriteLine("E-mailconfiguratie ontbreekt in omgevingsvariabelen.");
            return;
        }

        try
        {
            // Bouw een HTML-e-mail zodat links klikbaar zijn
            string vacancyListHtml = string.Join("\n", newVacancies.Select(v =>
                $"  <li><a href=\"{v.Link}\">{System.Net.WebUtility.HtmlEncode(v.Title)}</a></li>"
            ));

            string htmlBody = $"""
                <html>
                <body style="font-family: Arial, sans-serif; color: #222;">
                  <h2>🚨 Nieuwe Cogas vacature(s) gevonden!</h2>
                  <p>Er {(newVacancies.Count == 1 ? "is" : "zijn")} <strong>{newVacancies.Count}</strong>
                     nieuwe vacature(s) beschikbaar bij Cogas:</p>
                  <ul>
                {vacancyListHtml}
                  </ul>
                  <p style="color: #666; font-size: 0.9em;">
                    Bekijk alle vacatures op
                    <a href="https://werkenbij.cogas.nl/vacatures">werkenbij.cogas.nl</a>.
                  </p>
                </body>
                </html>
                """;

            var mailMessage = new MailMessage
            {
                From       = new MailAddress(senderEmail, "Cogas Vacature Melder"),
                Subject    = $"🚨 {newVacancies.Count} nieuwe Cogas vacature(s) gevonden!",
                Body       = htmlBody,
                IsBodyHtml = true
            };
            mailMessage.To.Add(receiverEmail);

            using var smtpClient = new SmtpClient("smtp.gmail.com")
            {
                Port        = 587,
                Credentials = new NetworkCredential(senderEmail, senderPassword),
                EnableSsl   = true
            };

            smtpClient.Send(mailMessage);
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
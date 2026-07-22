using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using Newtonsoft.Json;
using HtmlAgilityPack;

class Program
{
    // 1. Instellingen: De URL van Cogas en de naam van het lokale JSON-bestand
    private static readonly string Url = "https://werkenbij.cogas.nl/vacatures?functiegroep=";
    private static readonly string JsonFile = "vacancies.json";

    public static void Main(string[] args)
    {
        Console.WriteLine("Start vacature-controle...");

        // Stap A: Haal alle huidige vacatures op van de website
        var currentVacancies = GetCurrentVacancies();
        if (currentVacancies.Count == 0)
        {
            Console.WriteLine("Geen vacatures gevonden op de pagina.");
            return;
        }

        // Stap B: Laad de oude lijst uit 'vacancies.json' als die al bestaat
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

        // Stap C: Vergelijk de huidige lijst met de oude lijst om nieuwe vacatures te vinden
        var oldLinks = new HashSet<string>(oldVacancies.Select(v => v.Link));
        var newVacancies = currentVacancies.Where(v => !oldLinks.Contains(v.Link)).ToList();

        // Stap D: Als er nieuwe vacatures zijn, stuur een mail en update het JSON-bestand
        if (newVacancies.Count > 0)
        {
            Console.WriteLine($"Er zijn {newVacancies.Count} nieuwe vacatures gevonden!");
            SendNotification(newVacancies);

            // Sla de actuele volledige lijst op in het JSON-bestand voor de volgende keer
            string updatedJson = JsonConvert.SerializeObject(currentVacancies, Formatting.Indented);
            File.WriteAllText(JsonFile, updatedJson);
        }
        else
        {
            Console.WriteLine("Geen nieuwe vacatures gevonden.");
        }
    }

    /// <summary>
    /// Haalt de webpagina op en filtert alle vacature-links en titels eruit.
    /// </summary>
    private static List<JobVacancy> GetCurrentVacancies()
    {
        var vacancies = new List<JobVacancy>();
        try
        {
            using (var client = new WebClient())
            {
                // Stel een User-Agent in zodat de website de aanvraag niet blokkeert
                client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                string html = client.DownloadString(Url);

                // Laad de HTML in HtmlAgilityPack om er doorheen te kunnen zoeken
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Zoek naar alle <a> (link) elementen op de pagina
                var links = doc.DocumentNode.SelectNodes("//a[@href]");
                if (links != null)
                {
                    foreach (var linkNode in links)
                    {
                        string href = linkNode.GetAttributeValue("href", string.Empty);
                        
                        // We zijn alleen geïnteresseerd in links die het woord '/vacature/' bevatten
                        if (href.Contains("/vacature/"))
                        {
                            string title = HtmlEntity.DeEntitize(linkNode.InnerText.Trim());

                            // Controleer of de titel geldig is en niet te kort
                            if (!string.IsNullOrEmpty(title) && title.Length > 3)
                            {
                                // Als de link begint met een slash, maak er dan de volledige URL van
                                if (href.StartsWith("/"))
                                {
                                    href = "https://werkenbij.cogas.nl" + href;
                                }

                                var vacancy = new JobVacancy { Title = title, Link = href };
                                
                                // Voeg toe aan de lijst als hij er nog niet in staat (dubbelen voorkomen)
                                if (!vacancies.Any(v => v.Link == vacancy.Link))
                                {
                                    vacancies.Add(vacancy);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fout bij ophalen pagina: {ex.Message}");
        }

        return vacancies;
    }

    /// <summary>
    /// Verstuurt een e-mail via Gmail SMTP met behulp van de GitHub Secrets (omgevingsvariabelen).
    /// </summary>
    private static void SendNotification(List<JobVacancy> newVacancies)
    {
        // Haal de inloggegevens op uit de omgevingsvariabelen
        string senderEmail = Environment.GetEnvironmentVariable("MAIL_USERNAME");
        string senderPassword = Environment.GetEnvironmentVariable("MAIL_PASSWORD");
        string receiverEmail = Environment.GetEnvironmentVariable("MAIL_TO");

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

            // Bouw de inhoud van de e-mail op
            string body = "Er zijn nieuwe vacatures gevonden bij Cogas:\n\n";
            foreach (var v in newVacancies)
            {
                body += $"- {v.Title}\n  {v.Link}\n\n";
            }
            mailMessage.Body = body;

            // Maak verbinding met de Gmail SMTP-server en verstuur de mail
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

// Hulpmodel om de structuur van een vacature vast te leggen in JSON
public class JobVacancy
{
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("link")]
    public string Link { get; set; } = string.Empty;
}
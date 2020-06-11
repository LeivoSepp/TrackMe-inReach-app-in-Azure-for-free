using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using SendGrid.Helpers.Mail;
using System.Linq;
using System.Collections.Generic;

namespace TrackMePublicFunctions.TrackMe
{
    public class Emails
    {
        public string EmailSubject { get; set; }
        public string EmailBody { get; set; }
        public string UserWebId { get; set; }
        public string DateTime { get; set; }
        public string Name { get; set; }
        public string EmailFrom { get; set; }
        public string[] EmailTo { get; set; }
    }
    public static class SendEmailInReach
    {
        [FunctionName("SendEmailInReach")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [SendGrid(ApiKey = "SendGridAPIKey")] IAsyncCollector<SendGridMessage> messageCollector
            )
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            List<Emails> emails = JsonConvert.DeserializeObject<List<Emails>>(requestBody);

            foreach (var email in emails)
            {
                var emailSubscribers = email.EmailTo.Select(x => new EmailAddress(x)).ToList();
                var message = new SendGridMessage();
                message.AddTo(email.EmailFrom, email.Name);
                message.AddBccs(emailSubscribers);
                message.SetFrom(email.EmailFrom, email.Name);
                message.AddContent("text/html", email.EmailBody);
                message.SetSubject(email.EmailSubject);
                await messageCollector.AddAsync(message);
            }
            return new OkObjectResult("OK");
        }
    }
}

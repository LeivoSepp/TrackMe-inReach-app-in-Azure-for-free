using System;
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
using Microsoft.Azure.Documents;

namespace TrackMePublicFunctions.TrackMe
{
    public class Emails
    {
        public string EmailSubject { get; set; }
        public string EmailBody { get; set; }
        public string UserWebId { get; set; }
        public string DateTime { get; set; }
    }
    public static class SendEmailInReach
    {
        [FunctionName("SendEmailInReach")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
                )] IEnumerable<InReachUser> users,
            [SendGrid(ApiKey = "SendGridAPIKey")] IAsyncCollector<SendGridMessage> messageCollector
            )
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            List<Emails> emails = JsonConvert.DeserializeObject<List<Emails>>(requestBody);
            
            //remove all duplicates by DateTime field.
            List<Emails> emailList = emails.GroupBy(x => x.DateTime).Select(x => x.First()).ToList();
            foreach (var email in emailList)
            {
                var message = new SendGridMessage();
                InReachUser user = users.First(x => x.userWebId == email.UserWebId);
                var emailSubscribers = user.subscibers.Select(x => new EmailAddress(x)).ToList();

                message.AddTo(user.email, user.name);
                message.AddBccs(emailSubscribers);
                message.SetFrom(user.email, user.name);

                message.AddContent("text/html", email.EmailBody);
                message.SetSubject(email.EmailSubject);
                await messageCollector.AddAsync(message);
            }
            return new OkObjectResult("OK");
        }
    }
}

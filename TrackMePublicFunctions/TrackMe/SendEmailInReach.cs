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

namespace TrackMePublicFunctions.TrackMe
{
    public static class SendEmailInReach
    {
        [FunctionName("SendEmailInReach")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "SendEmailInReach/{userWebId}")] HttpRequest req,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user' and c.userWebId = {userWebId}"
                )] IEnumerable<InReachUser> users,

            [SendGrid(ApiKey = "SendGridAPIKey")] IAsyncCollector<SendGridMessage> messageCollector
            )
        {
            var user = users.First();

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            
            string subject = data?.eMailSubject;
            string messageBody = data?.eMailMessage;

            var message = new SendGridMessage();

            var emailSubscribers = user.subscibers.Select(x => new EmailAddress(x)).ToList();
            
            message.AddTo(user.email, user.name);
            message.AddBccs(emailSubscribers);
            message.SetFrom(user.email, user.name);

            message.AddContent("text/html", messageBody);
            message.SetSubject(subject);
            await messageCollector.AddAsync(message);

            return new OkObjectResult(message);
        }
    }
}

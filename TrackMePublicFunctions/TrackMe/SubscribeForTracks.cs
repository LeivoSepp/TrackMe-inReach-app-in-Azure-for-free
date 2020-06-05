using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace TrackMePublicFunctions.TrackMe
{
    public class InReachUser
    {
        public string InReachWebAddress { get; set; }
        public string InReachWebPassword { get; set; }
        public string email { get; set; }
        public string name { get; set; }
        public string userWebId { get; set; }
        public bool Active { get; set; }
        public string id { get; set; }
        public string groupid { get; set; }
        public List<string> subscibers { get; set; }
        public string status { get; set; }
    }
    public static class SubscribeForTracks
    {
        [FunctionName("SubscribeForTracks")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "SubscribeForTracks/{userWebId}/{email}")] HttpRequest req,
            string email,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user' and c.userWebId = {userWebId}"
            )] IEnumerable<InReachUser> users,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection"
            )] IAsyncCollector<InReachUser> output,
            ILogger log)
        {
            InReachUser user = users.First();

            if (user.subscibers == null)
            {
                List<string> newList = new List<string>();
                newList.Add(email);
                user.subscibers = newList;
            }
            else
            {
                user.subscibers.Add(email);
            }
            
            await output.AddAsync(user);

            return new OkObjectResult(email);
        }
    }
}

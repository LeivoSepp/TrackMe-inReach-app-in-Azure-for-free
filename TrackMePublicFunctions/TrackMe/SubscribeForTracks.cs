using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
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
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user' and c.userWebId = {userWebId}"
            )] IEnumerable<InReachUser> inReachUsers,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree"
            )] IAsyncCollector<InReachUser> asyncCollectorInReachUser
            )
        {
            InReachUser user = inReachUsers.First();

            if (user.subscibers == null)
            {
                List<string> newList = new List<string>
                {
                    email
                };
                user.subscibers = newList;
            }
            else
            {
                user.subscibers.Add(email);
            }
            
            await asyncCollectorInReachUser.AddAsync(user);

            return new OkObjectResult(email);
        }
    }
}

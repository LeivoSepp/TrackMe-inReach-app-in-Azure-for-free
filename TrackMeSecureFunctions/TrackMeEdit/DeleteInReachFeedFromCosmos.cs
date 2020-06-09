using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents;
using System.Collections.Generic;
using System.Security.Claims;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public class Document_self
    {
        public string _self { get; set; }
    }

    public static class DeleteInReachFeedFromCosmos
    {
        [FunctionName("DeleteInReachFeedFromCosmos")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "DeleteInReachFeedFromCosmos/{userWebId}/{trackId}")] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                PartitionKey = "{userWebId}",
                Id = "{trackId}"
                )] Document_self document,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
            )] IEnumerable<InReachUser> users,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree"
            )] DocumentClient client)
        {
            ClaimsPrincipal Identities = req.HttpContext.User;
            var checkUser = new HelperCheckUser();
            var LoggedInUser = checkUser.LoggedInUser(users, Identities);
            var IsAuthenticated = false;
            if (LoggedInUser.status == Status.ExistingUser)
                IsAuthenticated = true;

            if (IsAuthenticated)
            {
                //using GET and URL querystring parameter "id" to get the document selflink
                string selfLink = document._self;

                //selfLink is like this: "dbs/auo6AA==/colls/auo6AOdfluE=/docs/auo6AOdfluEnFwIAAAAAAA==/";
                await client.DeleteDocumentAsync(selfLink, new RequestOptions { PartitionKey = new PartitionKey(LoggedInUser.userWebId) });
            }
            return new OkObjectResult(IsAuthenticated);
        }
    }
}

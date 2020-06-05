using System;
using System.IO;
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
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                PartitionKey = "{userWebId}",
                Id = "{trackId}"
                )] Document_self document,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
            )] IEnumerable<InReachUser> users,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection"
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
            else
            {
                //do something if not authenticated
            }
            return new OkObjectResult(IsAuthenticated);
        }
    }
}

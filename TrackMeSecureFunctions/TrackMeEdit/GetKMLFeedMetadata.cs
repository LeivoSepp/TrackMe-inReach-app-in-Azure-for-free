using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Security.Claims;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public class KMLmetadata
    {
        public string id { get; set; }
        public string Title { get; set; }
        public string d1 { get; set; }
        public string d2 { get; set; }
        public string Track { get; set; }
        public string InReachWebAddress { get; set; }
        public string InReachWebPassword { get; set; }
    }

    public static class GetKMLFeedMetadata
    {
        [FunctionName("GetKMLFeedMetadata")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetKMLFeedMetadata/{GroupId}/{id}")] HttpRequest req,
            string GroupId,
             [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                PartitionKey = "{GroupId}",
                Id = "{id}"
                )]
            KMLmetadata input,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
            )] IEnumerable<InReachUser> users,

            ILogger log)
        {
            ClaimsPrincipal Identities = req.HttpContext.User;
            var checkUser = new HelperCheckUser();
            var LoggedInUser = checkUser.LoggedInUser(users, Identities);
            var IsAuthenticated = false;
            if (LoggedInUser.status == Status.ExistingUser)
                IsAuthenticated = true;

            if (IsAuthenticated)
            {
                if(GroupId == LoggedInUser.userWebId)
                    return new OkObjectResult(input);
                else
                    return new OkObjectResult("Not your track");
            }
            return new OkObjectResult(IsAuthenticated);
        }
    }
}

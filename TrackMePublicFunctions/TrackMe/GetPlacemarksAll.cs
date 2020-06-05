using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TrackMePublicFunctions.TrackMe
{
    public static class GetPlacemarksAll
    {
        [FunctionName("GetPlacemarksAll")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetPlacemarksAll/{GroupId}/{id}")] HttpRequest req,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                PartitionKey = "{GroupId}",
                Id = "{id}"
                )]
            KMLInfo input,

            ILogger log)
        {
            return new OkObjectResult(input.PlacemarksAll);
        }
    }
}

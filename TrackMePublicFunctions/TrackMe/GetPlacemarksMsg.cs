using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;

namespace TrackMePublicFunctions.TrackMe
{
    public class PlacemarkMsg
    {
        public string PlacemarksWithMessages { get; set; }
    }
    public static class GetPlacemarksMsg
    {
        [FunctionName("GetPlacemarksMsg")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetPlacemarksMsg/{GroupId}/{id}")] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                PartitionKey = "{GroupId}",
                Id = "{id}"
                )]
            PlacemarkMsg input)
        {
            return new OkObjectResult(input.PlacemarksWithMessages);
        }
    }
}

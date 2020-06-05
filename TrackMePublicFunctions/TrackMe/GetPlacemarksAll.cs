using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;

namespace TrackMePublicFunctions.TrackMe
{
    public class PlacemarkAll
    {
        public string PlacemarksAll { get; set; }
    }
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
            PlacemarkAll input)
        {
            return new OkObjectResult(input.PlacemarksAll);
        }
    }
}

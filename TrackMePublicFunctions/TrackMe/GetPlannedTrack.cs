using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;

namespace TrackMePublicFunctions.TrackMe
{
    public class PlacemarkTrack
    {
        public string Track { get; set; }
    }
    public static class GetPlannedTrack
    {
        [FunctionName("GetPlannedTrack")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetPlannedTrack/{GroupId}/{id}")] HttpRequest req,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                PartitionKey = "{GroupId}",
                Id = "{id}"
                )]
            PlacemarkTrack input)
        {
            return new OkObjectResult(input.Track);
        }
    }
}

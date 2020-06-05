using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;

namespace TrackMePublicFunctions.TrackMe
{
    public class PlacemarkTrackLine
    {
        public string LineString { get; set; }
    }
    public static class GetTrackLine
    {
        [FunctionName("GetTrackLine")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetTrackLine/{GroupId}/{id}")] HttpRequest req,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                PartitionKey = "{GroupId}",
                Id = "{id}"
                )]
            PlacemarkTrackLine input)
        {
            return new OkObjectResult(input.LineString);
        }
    }
}

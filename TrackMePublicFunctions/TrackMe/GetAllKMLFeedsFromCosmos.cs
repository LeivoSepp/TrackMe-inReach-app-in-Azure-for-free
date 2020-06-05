using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace TrackMePublicFunctions.TrackMe
{
    public static class GetAllKMLFeedsFromCosmos
    {
        [FunctionName("GetAllKMLFeedsFromCosmos")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetAllKMLFeedsFromCosmos/{userWebId}")] HttpRequest req,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT c.id, c.Title, c.d1, c.d2 FROM c WHERE c.groupid = {userWebId} ORDER BY c.d1 DESC"
                )]
                JArray input,
            ILogger log)
        {
            return new OkObjectResult(input);
        }
    }
}

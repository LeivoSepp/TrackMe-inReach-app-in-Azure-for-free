using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Web;
using System.Text;
using System.Globalization;
using System.Security.Claims;
using System.IO;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public static class SetInReachFeed
    {
        private static HelperKMLParse helperKMLParse = new HelperKMLParse();

        //this function will remove all weird characters from the id field
        public static string UrlEncode(string value)
        {
            string reservedCharacters = " -!*'();:@&=+$,/?%#[]€£${}._~Ž^§½<>|";
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder();

            foreach (char @char in value)
            {
                if (reservedCharacters.IndexOf(@char) == -1)
                    sb.Append(@char);
            }
            return sb.ToString();
        }
        //this function replace all special characters to the closest unicode character
        //for example: ä->a õ->o etc
        public static string RemoveDiacritics(string s)
        {
            string normalizedString = s.Normalize(NormalizationForm.FormD);
            StringBuilder stringBuilder = new StringBuilder();

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    stringBuilder.Append(c);
            }
            return stringBuilder.ToString();
        }
        //taking POST formdata and saves it into CosmosDB
        [FunctionName("SetInReachFeed")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree"
                )]
                IAsyncCollector<KMLInfo> asyncCollectorKMLInfo,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
            )] IEnumerable<InReachUser> inReachUsers,
            ExecutionContext context
            )
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
            var StorageContainerConnectionString = config["StorageContainerConnectionString"];

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageContainerConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            ClaimsPrincipal Identities = req.HttpContext.User;
            var checkUser = new HelperCheckUser();
            var LoggedInUser = checkUser.LoggedInUser(inReachUsers, Identities);
            var IsAuthenticated = false;
            if (LoggedInUser.status == Status.ExistingUser)
                IsAuthenticated = true;

            if (IsAuthenticated)
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (requestBody != "")
                {
                    var kMLFull = JsonConvert.DeserializeObject<KMLFull>(requestBody);

                    KMLInfo kMLInfo = new KMLInfo()
                    {
                        id = kMLFull.id,
                        Title = kMLFull.Title,
                        d1 = kMLFull.d1,
                        d2 = kMLFull.d2,
                        InReachWebAddress = kMLFull.InReachWebAddress,
                        InReachWebPassword = kMLFull.InReachWebPassword,
                        UserTimezone = kMLFull.UserTimezone
                    };
                    var blobs = helperKMLParse.Blobs;
                    blobs.ForEach(x => x.BlobValue = "");
                    blobs.First(x => x.BlobName == "plannedtrack").BlobValue = kMLFull.PlannedTrack;

                    //1. replace ö->o ä->a etc
                    //2. first: UrlEncode is removing all weird charactes and spaces
                    //3. second: HttpUtility.UrlEncode is removing some not named weird characters, just in case
                    //4. third: UrlEncode again is removing possible %-marks
                    //setting id field only on initial track creation
                    if (string.IsNullOrEmpty(kMLInfo.id))
                    {
                        string id = RemoveDiacritics(kMLInfo.Title);
                        id = UrlEncode(HttpUtility.UrlEncode(UrlEncode(id)));
                        kMLInfo.id = id;
                    }
                    kMLInfo.LastPointTimestamp = "";
                    kMLInfo.groupid = LoggedInUser.userWebId;
                    kMLInfo.IsLongTrack = false;

                    var dateD1 = DateTime.Parse(kMLInfo.d1).AddHours(-kMLInfo.UserTimezone);
                    var dateD2 = DateTime.Parse(kMLInfo.d2).AddDays(1).AddHours(-kMLInfo.UserTimezone);
                    TimeSpan timeSpan = dateD2 - dateD1;
                    //this setting affects of parsing all points (slow) or not. Depending on the duration of the track
                    if (timeSpan.TotalDays > 2)
                        kMLInfo.IsLongTrack = true;

                    kMLInfo.d1 = dateD1.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    kMLInfo.d2 = dateD2.ToString("yyyy-MM-ddTHH:mm:ssZ");

                    HelperGetKMLFromGarmin helperGetKMLFromGarmin = new HelperGetKMLFromGarmin();
                    //get feed grom garmin
                    var kmlFeedresult = await helperGetKMLFromGarmin.GetKMLAsync(kMLInfo);

                    //parse and transform the feed and save to database
                    helperKMLParse.ParseKMLFile(kmlFeedresult, kMLInfo, blobs, new List<Emails>(), LoggedInUser);
                    await asyncCollectorKMLInfo.AddAsync(kMLInfo);
                    //save blobs
                    foreach (var blob in blobs)
                    {
                        var blobName = $"{kMLInfo.groupid}/{kMLInfo.id}/{blob.BlobName}.kml";
                        await helperKMLParse.AddToBlobAsync(blobName, blob.BlobValue, blobClient);
                    }
                }
            }
            return new OkObjectResult(IsAuthenticated);
        }
    }
}